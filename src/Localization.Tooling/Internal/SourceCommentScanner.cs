using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Recovers translation comments from a project's own C# source — the one place they live, since the compiler
/// strips comments from IL and the PDB, so they cannot come from the built assembly (Decision D-K reads the binary
/// for the strings themselves). A syntax-only parse (no compilation, no semantic model, no metadata references)
/// finds each comment written inside a call, indexer, or attribute argument list and associates it with the nearest
/// string literal in that list. The template build joins those literals to the extracted entries by identity, so a
/// comment beside a key or a default reaches its catalog entry without any source location.
/// </summary>
internal static class SourceCommentScanner
{
    /// <summary>
    /// Scans every <c>.cs</c> file under <paramref name="sourceRoot"/> (build output excluded) and returns the
    /// comments indexed by the literal they sit beside. Unreadable files and parse errors are skipped — a comment
    /// is advisory, never worth failing an extract over.
    /// </summary>
    public static CommentIndex Scan(string sourceRoot)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return CommentIndex.Empty;
        }

        var byLiteral = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in EnumerateSources(sourceRoot))
        {
            ScanFile(file, byLiteral);
        }

        var index = byLiteral.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
        return new CommentIndex(index);
    }

    private static IEnumerable<string> EnumerateSources(string sourceRoot) =>
        Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories).Where(NotBuildOutput);

    // Skip obj/ and bin/: they hold generated and copied sources whose comments (if any) are not the author's.
    private static bool NotBuildOutput(string path)
    {
        for (var directory = Path.GetDirectoryName(path); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            var name = Path.GetFileName(directory);
            if (name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || name.Equals("bin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static void ScanFile(string path, Dictionary<string, List<string>> byLiteral)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        // A file with no comment marker or no string literal can carry no in-paren translation comment — skip the
        // parse entirely. The markers may appear inside strings; this only ever avoids a parse, never a real hit.
        if (!text.Contains('"') || (!text.Contains("//", StringComparison.Ordinal) && !text.Contains("/*", StringComparison.Ordinal)))
        {
            return;
        }

        SyntaxNode root = CSharpSyntaxTree.ParseText(text).GetRoot();
        foreach (SyntaxToken token in root.DescendantTokens())
        {
            foreach (SyntaxTrivia trivia in token.LeadingTrivia)
            {
                Associate(trivia, token, byLiteral);
            }

            foreach (SyntaxTrivia trivia in token.TrailingTrivia)
            {
                Associate(trivia, token, byLiteral);
            }
        }
    }

    private static void Associate(SyntaxTrivia trivia, SyntaxToken token, Dictionary<string, List<string>> byLiteral)
    {
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
        {
            return;
        }

        SyntaxNode? list = OwningArgumentList(token);
        if (list is null)
        {
            return;
        }

        var literal = NearestLiteral(list, trivia.SpanStart);
        if (literal is null)
        {
            return;
        }

        var comment = CommentText(trivia);
        if (comment.Length == 0)
        {
            return;
        }

        if (!byLiteral.TryGetValue(literal, out List<string>? comments))
        {
            comments = [];
            byLiteral[literal] = comments;
        }

        comments.Add(comment);
    }

    // The nearest enclosing call, indexer, or attribute argument list — the "( )" a comment must sit inside to be a
    // translation comment. A comment reached only by crossing a statement or member boundary is leading trivia, not
    // ours, and is ignored (leading trivia is deferred by design: ambiguous on calls, PDB-invisible on annotations).
    private static SyntaxNode? OwningArgumentList(SyntaxToken token)
    {
        for (SyntaxNode? node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is ArgumentListSyntax or BracketedArgumentListSyntax or AttributeArgumentListSyntax)
            {
                return node;
            }

            if (node is StatementSyntax or MemberDeclarationSyntax)
            {
                return null;
            }
        }

        return null;
    }

    private static string? NearestLiteral(SyntaxNode list, int position)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        var bestPreceding = false;
        foreach (ExpressionSyntax expression in ArgumentExpressions(list))
        {
            var value = ConstantString(expression);
            if (value is null)
            {
                continue;
            }

            var start = expression.Span.Start;
            var preceding = start <= position;
            var distance = Math.Abs(position - start);
            // Prefer a literal before the comment; among those the closest, then the closest after.
            if (Closer(preceding, distance, bestPreceding, bestDistance))
            {
                best = value;
                bestDistance = distance;
                bestPreceding = preceding;
            }
        }

        return best;
    }

    private static bool Closer(bool preceding, int distance, bool bestPreceding, int bestDistance)
    {
        if (preceding != bestPreceding)
        {
            return preceding;
        }

        return distance < bestDistance;
    }

    private static IEnumerable<ExpressionSyntax> ArgumentExpressions(SyntaxNode list) => list switch
    {
        ArgumentListSyntax args => args.Arguments.Select(argument => argument.Expression),
        BracketedArgumentListSyntax bracketed => bracketed.Arguments.Select(argument => argument.Expression),
        AttributeArgumentListSyntax attribute => attribute.Arguments.Select(argument => argument.Expression),
        _ => []
    };

    // A compile-time constant string: a string literal, or "+" concatenation of constant strings. Anything else
    // (a const-field reference, interpolation, nameof) is not recoverable from syntax alone and yields null — those
    // sites simply get no comment, the same graceful miss as a site with none.
    private static string? ConstantString(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) =>
            ConstantString(binary.Left) is { } left && ConstantString(binary.Right) is { } right ? left + right : null,
        ParenthesizedExpressionSyntax parenthesized => ConstantString(parenthesized.Expression),
        _ => null
    };

    private static string CommentText(SyntaxTrivia trivia)
    {
        var raw = trivia.ToString();
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
        {
            return raw.Length >= 2 ? raw[2..].Trim() : "";
        }

        // Multi-line: strip the leading "/*" and trailing "*/".
        return raw.Length >= 4 ? raw[2..^2].Trim() : "";
    }
}
