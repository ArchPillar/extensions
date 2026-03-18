using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Mapper.Generators;

/// <summary>
/// Analyzes a partial MapperContext subclass to extract mapper, enum-mapper,
/// and variable declarations from the class body and constructor.
/// </summary>
internal static class Analyzer
{
    internal static void AnalyzeClass(
        ClassDeclarationSyntax classDecl,
        SemanticModel model,
        MapperContextInfo info,
        CancellationToken ct)
    {
        // Pass 1: discover Variable<T> properties
        foreach (var member in classDecl.Members)
        {
            ct.ThrowIfCancellationRequested();
            if (!(member is PropertyDeclarationSyntax propDecl))
                continue;

            var propSymbol = model.GetDeclaredSymbol(propDecl, ct);
            if (propSymbol == null)
                continue;

            var propType = propSymbol.Type as INamedTypeSymbol;
            if (propType == null || !propType.IsGenericType)
                continue;

            if (propType.ConstructedFrom.ToDisplayString() == "ArchPillar.Extensions.Mapper.Variable<T>")
            {
                var typeArg = propType.TypeArguments[0];
                info.Variables.Add(new VariableInfo(propSymbol.Name, typeArg.ToDisplayString()));
            }
        }

        // Pass 2: analyze constructors for mapper assignments
        foreach (var member in classDecl.Members)
        {
            ct.ThrowIfCancellationRequested();
            if (!(member is ConstructorDeclarationSyntax ctorDecl))
                continue;
            if (ctorDecl.Body == null)
                continue;

            foreach (var statement in ctorDecl.Body.Statements)
            {
                ct.ThrowIfCancellationRequested();
                AnalyzeStatement(statement, model, info, ct);
            }
        }
    }

    private static void AnalyzeStatement(
        StatementSyntax statement,
        SemanticModel model,
        MapperContextInfo info,
        CancellationToken ct)
    {
        if (!(statement is ExpressionStatementSyntax exprStmt))
            return;

        if (!(exprStmt.Expression is AssignmentExpressionSyntax assignment))
            return;

        var propertyName = GetAssignmentTarget(assignment.Left);
        if (propertyName == null)
            return;

        AnalyzeRightHandSide(propertyName, assignment.Right, model, info, ct);
    }

    private static string GetAssignmentTarget(ExpressionSyntax left)
    {
        if (left is IdentifierNameSyntax id)
            return id.Identifier.Text;

        if (left is MemberAccessExpressionSyntax ma && ma.Expression is ThisExpressionSyntax)
            return ma.Name.Identifier.Text;

        return null!;
    }

    private static void AnalyzeRightHandSide(
        string propertyName,
        ExpressionSyntax expression,
        SemanticModel model,
        MapperContextInfo info,
        CancellationToken ct)
    {
        // Collect fluent method calls (.Map(), .Optional(), .Ignore()) while walking
        // inward to find the CreateMapper/CreateEnumMapper call.
        var fluentCalls = new List<InvocationExpressionSyntax>();
        var current = expression;

        while (current is InvocationExpressionSyntax invocation)
        {
            // Check method symbol first
            var symbolInfo = model.GetSymbolInfo(invocation, ct);
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

            if (methodSymbol == null)
                break;

            var methodName = methodSymbol.Name;

            if (methodName == "CreateMapper")
            {
                ProcessCreateMapper(propertyName, invocation, methodSymbol, fluentCalls, info);
                return;
            }

            if (methodName == "CreateEnumMapper")
            {
                ProcessCreateEnumMapper(propertyName, methodSymbol, info);
                return;
            }

            // It's a fluent method (Map, Optional, Ignore, SetCoverageValidation)
            if (methodName == "Map" || methodName == "Optional" || methodName == "Ignore"
                || methodName == "SetCoverageValidation")
            {
                fluentCalls.Add(invocation);

                // Walk inward: the receiver is the expression before ".Method("
                if (invocation.Expression is MemberAccessExpressionSyntax access)
                {
                    current = access.Expression;
                    continue;
                }
            }

            break;
        }
    }

    private static void ProcessCreateMapper(
        string propertyName,
        InvocationExpressionSyntax createMapperCall,
        IMethodSymbol methodSymbol,
        List<InvocationExpressionSyntax> fluentCalls,
        MapperContextInfo info)
    {
        var typeArgs = methodSymbol.TypeArguments;
        if (typeArgs.Length != 2) return;

        var sourceType = typeArgs[0].ToDisplayString();
        var destType = typeArgs[1].ToDisplayString();
        var mapperInfo = new MapperInfo(propertyName, sourceType, destType);

        // Extract member-init bindings from the lambda argument
        if (createMapperCall.ArgumentList.Arguments.Count > 0)
        {
            var arg = createMapperCall.ArgumentList.Arguments[0].Expression;
            ExtractLambdaMappings(arg, mapperInfo);
        }

        // Process fluent calls (collected outermost-first, reverse to get declaration order)
        fluentCalls.Reverse();
        foreach (var call in fluentCalls)
        {
            ProcessFluentCall(call, mapperInfo);
        }

        info.Mappers.Add(mapperInfo);
    }

    private static void ExtractLambdaMappings(ExpressionSyntax lambdaExpr, MapperInfo mapperInfo)
    {
        string srcParamName;
        SyntaxNode body;

        if (lambdaExpr is SimpleLambdaExpressionSyntax simple)
        {
            srcParamName = simple.Parameter.Identifier.Text;
            body = simple.Body;
        }
        else if (lambdaExpr is ParenthesizedLambdaExpressionSyntax paren
            && paren.ParameterList.Parameters.Count == 1)
        {
            srcParamName = paren.ParameterList.Parameters[0].Identifier.Text;
            body = paren.Body;
        }
        else
        {
            return;
        }

        mapperInfo.SourceParameterName = srcParamName;

        // Find the object initializer
        var initializer = FindInitializer(body);
        if (initializer == null)
            return;

        foreach (var expr in initializer.Expressions)
        {
            if (expr is AssignmentExpressionSyntax assign
                && assign.Left is IdentifierNameSyntax propId)
            {
                mapperInfo.Mappings.Add(new PropertyMappingInfo(
                    propId.Identifier.Text,
                    assign.Right.ToFullString().Trim(),
                    MappingKind.Required));
            }
        }
    }

    private static InitializerExpressionSyntax FindInitializer(SyntaxNode body)
    {
        if (body is ObjectCreationExpressionSyntax oc && oc.Initializer != null)
            return oc.Initializer;

        if (body is ImplicitObjectCreationExpressionSyntax ioc && ioc.Initializer != null)
            return ioc.Initializer;

        // Search direct children
        foreach (var child in body.ChildNodes())
        {
            if (child is ObjectCreationExpressionSyntax oc2 && oc2.Initializer != null)
                return oc2.Initializer;

            if (child is ImplicitObjectCreationExpressionSyntax ioc2 && ioc2.Initializer != null)
                return ioc2.Initializer;
        }

        return null!;
    }

    private static void ProcessFluentCall(InvocationExpressionSyntax call, MapperInfo mapperInfo)
    {
        if (!(call.Expression is MemberAccessExpressionSyntax access))
            return;

        var name = access.Name.Identifier.Text;
        var args = call.ArgumentList.Arguments;

        if ((name == "Optional" || name == "Map") && args.Count == 2)
        {
            var destProp = ExtractLambdaPropertyName(args[0].Expression);
            if (destProp != null)
            {
                var kind = name == "Optional" ? MappingKind.Optional : MappingKind.Required;
                string paramName;
                var bodyText = ExtractSourceLambdaBodyAndParam(args[1].Expression, out paramName);
                mapperInfo.Mappings.Add(new PropertyMappingInfo(
                    destProp, bodyText, kind, paramName));
            }
        }
        else if (name == "Ignore" && args.Count == 1)
        {
            var destProp = ExtractLambdaPropertyName(args[0].Expression);
            if (destProp != null)
            {
                mapperInfo.Mappings.Add(new PropertyMappingInfo(destProp, null, MappingKind.Ignored));
            }
        }
    }

    private static string ExtractLambdaPropertyName(ExpressionSyntax expr)
    {
        ExpressionSyntax body = null!;

        if (expr is SimpleLambdaExpressionSyntax simple)
            body = simple.ExpressionBody!;
        else if (expr is ParenthesizedLambdaExpressionSyntax paren)
            body = paren.ExpressionBody!;

        if (body is MemberAccessExpressionSyntax ma)
            return ma.Name.Identifier.Text;

        return null!;
    }

    private static string ExtractSourceLambdaBodyAndParam(ExpressionSyntax expr, out string paramName)
    {
        if (expr is SimpleLambdaExpressionSyntax simple && simple.ExpressionBody != null)
        {
            paramName = simple.Parameter.Identifier.Text;
            return simple.ExpressionBody.ToFullString().Trim();
        }

        if (expr is ParenthesizedLambdaExpressionSyntax paren
            && paren.ExpressionBody != null
            && paren.ParameterList.Parameters.Count == 1)
        {
            paramName = paren.ParameterList.Parameters[0].Identifier.Text;
            return paren.ExpressionBody.ToFullString().Trim();
        }

        paramName = null!;
        return expr.ToFullString().Trim();
    }

    private static void ProcessCreateEnumMapper(
        string propertyName,
        IMethodSymbol methodSymbol,
        MapperContextInfo info)
    {
        var typeArgs = methodSymbol.TypeArguments;
        if (typeArgs.Length != 2)
            return;

        info.EnumMappers.Add(new EnumMapperInfo(
            propertyName,
            typeArgs[0].ToDisplayString(),
            typeArgs[1].ToDisplayString()));
    }
}
