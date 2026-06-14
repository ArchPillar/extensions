using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace ArchPillar.Extensions.Localization.CodeFixes;

/// <summary>
/// Offers a fix for <c>APL0010</c> (a <c>Localized&lt;T&gt;</c> bundle that should be <c>partial</c> so its
/// localizer constructor and DI registration are generated): it adds the <c>partial</c> modifier to the class
/// declaration, leaving everything else intact.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MarkLocalizedPartialCodeFixProvider))]
[Shared]
public sealed class MarkLocalizedPartialCodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "APL0010";

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
            ClassDeclarationSyntax? declaration = node as ClassDeclarationSyntax ?? node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            if (declaration is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Mark partial",
                    token => AddPartialAsync(context.Document, root, declaration, token),
                    equivalenceKey: DiagnosticId),
                diagnostic);
        }
    }

    private static Task<Document> AddPartialAsync(
        Document document,
        SyntaxNode root,
        ClassDeclarationSyntax declaration,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        SyntaxGenerator generator = SyntaxGenerator.GetGenerator(document);
        SyntaxNode updated = generator.WithModifiers(declaration, generator.GetModifiers(declaration).WithPartial(true));
        return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(declaration, updated)));
    }
}
