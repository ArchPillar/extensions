using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Mapper.Generators;

[Generator]
public sealed class MapperContextGenerator : IIncrementalGenerator
{
    private const string MapperContextFullName = "ArchPillar.Extensions.Mapper.MapperContext";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var partialContexts = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsPartialClassCandidate(node),
                transform: static (ctx, ct) => GetMapperContextInfo(ctx, ct))
            .Where(static info => info != null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(partialContexts, static (spc, info) =>
        {
            var source = Emitter.GenerateSource(info);
            spc.AddSource(info.ClassName + ".g.cs", source);
        });
    }

    private static bool IsPartialClassCandidate(SyntaxNode node)
    {
        if (!(node is ClassDeclarationSyntax classDecl))
            return false;

        return classDecl.Modifiers.Any(SyntaxKind.PartialKeyword)
            && classDecl.BaseList != null;
    }

    private static MapperContextInfo GetMapperContextInfo(
        GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (classSymbol == null)
            return null!;

        if (!InheritsFrom(classSymbol, MapperContextFullName))
            return null!;

        var info = new MapperContextInfo(
            classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol.Name,
            classSymbol.DeclaredAccessibility);

        Analyzer.AnalyzeClass(classDecl, ctx.SemanticModel, info, ct);

        if (info.Mappers.Count == 0)
            return null!;

        return info;
    }

    private static bool InheritsFrom(INamedTypeSymbol symbol, string baseFullName)
    {
        var current = symbol.BaseType;
        while (current != null)
        {
            if (current.ToDisplayString() == baseFullName)
                return true;
            current = current.BaseType;
        }
        return false;
    }
}
