using System.Collections.Immutable;
using System.Composition;
using ArchPillar.Extensions.Localization.MessageFormat;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Localization.CodeFixes;

/// <summary>
/// Offers a fix for <c>APL0005</c> (a plural/select construct missing its <c>other</c> branch): it adds an
/// empty <c>other {}</c> branch to the flagged default-message literal, leaving the rest of the text intact
/// so a translator only has to fill the branch in. The fix is purely structural and never invents wording.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingOtherCodeFixProvider))]
[Shared]
public sealed class MissingOtherCodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "APL0005";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticId);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Add 'other' branch",
                        token => AddOtherBranchAsync(context.Document, root, literal, token),
                        equivalenceKey: DiagnosticId),
                    diagnostic);
            }
        }
    }

    private static Task<Document> AddOtherBranchAsync(
        Document document,
        SyntaxNode root,
        LiteralExpressionSyntax literal,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var rewritten = MessageSyntax.InsertMissingOtherBranches(literal.Token.ValueText);
        LiteralExpressionSyntax replacement = SyntaxFactory
            .LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(rewritten))
            .WithTriviaFrom(literal);
        return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(literal, replacement)));
    }
}
