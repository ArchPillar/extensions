using System;

namespace ArchPillar.Extensions.Mapper.Generators;

/// <summary>
/// Transforms source expression text extracted from user code into
/// AOT-compatible expressions that use generated methods instead of
/// mapper instance calls.
/// </summary>
internal static class ExpressionTransformer
{
    /// <summary>
    /// Transforms a source expression string into its AOT equivalent by:
    /// 1. Replacing nested Mapper.Map(x) calls with AotMap_Mapper(x, vars)
    /// 2. Replacing .Project(Mapper) with .Select(item => AotMap_Mapper(item, vars))
    /// 3. Replacing Variable references with AotHelper.GetVariable calls
    /// </summary>
    internal static string Transform(string expr, string srcParam, MapperContextInfo info)
    {
        // 1. Replace nested mapper .Map() calls
        foreach (var mapper in info.Mappers)
        {
            var mapCall = mapper.PropertyName + ".Map(";
            var idx = expr.IndexOf(mapCall, StringComparison.Ordinal);
            while (idx >= 0)
            {
                // Check that it's not part of a larger identifier (e.g., SomeProduct.Map())
                if (idx > 0 && IsIdentifierChar(expr[idx - 1]))
                {
                    idx = expr.IndexOf(mapCall, idx + mapCall.Length, StringComparison.Ordinal);
                    continue;
                }

                var parenStart = idx + mapCall.Length - 1; // position of '('
                var closeParen = FindMatchingParen(expr, parenStart);
                if (closeParen < 0) break;

                var arg = expr.Substring(parenStart + 1, closeParen - parenStart - 1);
                var replacement = "AotMap_" + mapper.PropertyName + "(" + arg + ", vars)";
                expr = expr.Substring(0, idx) + replacement + expr.Substring(closeParen + 1);

                idx = expr.IndexOf(mapCall, idx + replacement.Length, StringComparison.Ordinal);
            }
        }

        // 2. Replace .Project(MapperProperty) with .Select(item => AotMap_MapperProperty(item, vars))
        foreach (var mapper in info.Mappers)
        {
            var projectCall = ".Project(" + mapper.PropertyName + ")";
            if (expr.IndexOf(projectCall, StringComparison.Ordinal) >= 0)
            {
                var replacement = ".Select(__item__ => AotMap_" + mapper.PropertyName + "(__item__, vars)!)";
                expr = expr.Replace(projectCall, replacement);
            }
        }

        // 3. Replace Variable<T> references with AotHelper.GetVariable<T>(vars, variable, default)
        foreach (var variable in info.Variables)
        {
            expr = ReplaceVariableReferences(expr, variable);
        }

        return expr;
    }

    private static string ReplaceVariableReferences(string expr, VariableInfo variable)
    {
        var name = variable.Name;
        var idx = expr.IndexOf(name, StringComparison.Ordinal);
        while (idx >= 0)
        {
            // Must not be part of a larger identifier
            var before = idx > 0 ? expr[idx - 1] : ' ';
            var afterIdx = idx + name.Length;
            var after = afterIdx < expr.Length ? expr[afterIdx] : ' ';

            if (IsIdentifierChar(before) || IsIdentifierChar(after) || after == '.')
            {
                idx = expr.IndexOf(name, afterIdx, StringComparison.Ordinal);
                continue;
            }

            var replacement = "global::ArchPillar.Extensions.Mapper.AotHelper.GetVariable<"
                + variable.TypeName + ">(vars, " + name + ", " + name + ".DefaultValue ?? default!)";
            expr = expr.Substring(0, idx) + replacement + expr.Substring(afterIdx);
            idx = expr.IndexOf(name, idx + replacement.Length, StringComparison.Ordinal);
        }

        return expr;
    }

    private static int FindMatchingParen(string expr, int openParenIdx)
    {
        var depth = 0;
        for (var i = openParenIdx; i < expr.Length; i++)
        {
            if (expr[i] == '(') depth++;
            else if (expr[i] == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static bool IsIdentifierChar(char c)
    {
        return (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= '0' && c <= '9')
            || c == '_';
    }
}
