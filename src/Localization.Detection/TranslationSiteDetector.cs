using System.Collections.Immutable;
using System.Text;
using ArchPillar.Extensions.Localization.MessageFormat;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ArchPillar.Extensions.Localization.Detection;

/// <summary>
/// The single definition of "what is translatable". Given a compilation (or one node), it recognizes
/// calls that bind a compile-time constant to a <c>[Translatable]</c> parameter and resolves the key,
/// default, context, comment, and placeholders. The analyzer, the generator, and the tool all use this
/// so they agree byte-for-byte.
/// </summary>
public static class TranslationSiteDetector
{
    /// <summary>
    /// Detects every translation site in <paramref name="compilation"/>.
    /// </summary>
    /// <param name="compilation">The compilation to inspect.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>One result per recognized call site.</returns>
    public static IEnumerable<TranslationSiteResult> Detect(Compilation compilation, CancellationToken cancellationToken)
    {
        var symbols = AttributeSymbols.From(compilation);
        if (symbols.Translatable is null && symbols.StringLocalizer is null)
        {
            yield break;
        }

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (SyntaxNode node in tree.GetRoot(cancellationToken).DescendantNodes())
            {
                if (node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ElementAccessExpressionSyntax)
                {
                    TranslationSiteResult? result = DetectCore(model, node, symbols, cancellationToken);
                    if (result is not null)
                    {
                        yield return result;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Detects the translation site at <paramref name="node"/>, if any (used by the analyzer per node).
    /// </summary>
    /// <param name="model">The semantic model for the node's tree.</param>
    /// <param name="node">An invocation or object-creation node.</param>
    /// <param name="cancellationToken">A token to cancel the analysis.</param>
    /// <returns>The result, or <see langword="null"/> when the node is not a translation site.</returns>
    public static TranslationSiteResult? DetectAt(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        var symbols = AttributeSymbols.From(model.Compilation);
        return symbols.Translatable is null && symbols.StringLocalizer is null
            ? null
            : DetectCore(model, node, symbols, cancellationToken);
    }

    private static TranslationSiteResult? DetectCore(
        SemanticModel model,
        SyntaxNode node,
        AttributeSymbols symbols,
        CancellationToken cancellationToken)
    {
        IOperation? operation = model.GetOperation(node, cancellationToken);
        if (operation is IPropertyReferenceOperation propertyReference)
        {
            return DetectStringLocalizer(propertyReference, symbols, node);
        }

        ImmutableArray<IArgumentOperation> arguments = ExtractArguments(operation);
        if (arguments.IsDefault)
        {
            return null;
        }

        IArgumentOperation? keyArgument = FindArgument(arguments, symbols.Translatable);
        return keyArgument is null ? null : Build(arguments, keyArgument, symbols, node, ReceiverType(operation));
    }

    // IStringLocalizer interop: recognize the BCL indexer (this[name] / this[name, params object[]]) by
    // symbol, not attribute — it's a fixed shape we cannot annotate. The indexed literal is both the key and
    // the default (the .resx text-as-key convention; a not-found lookup returns the name). We extract only a
    // constant, valid-ICU literal and skip everything else silently: a dynamic key has no static text, and a
    // string.Format-style literal ({0:C}) isn't ICU — flagging either would break a migrating build (a
    // non-constant key is APL0001/Error on the native path). Category is typeof(T) for IStringLocalizer<T>,
    // global otherwise. Only the whole-compilation Detect walk reaches here (the analyzer ignores element
    // access), so existing IStringLocalizer code is extracted without lighting up editor diagnostics.
    private static TranslationSiteResult? DetectStringLocalizer(
        IPropertyReferenceOperation reference,
        AttributeSymbols symbols,
        SyntaxNode node)
    {
        if (symbols.StringLocalizer is null
            || !reference.Property.IsIndexer
            || !SymbolEqualityComparer.Default.Equals(reference.Property.ContainingType?.OriginalDefinition, symbols.StringLocalizer)
            || reference.Arguments.Length == 0)
        {
            return null;
        }

        Optional<object?> constant = reference.Arguments[0].Value.ConstantValue;
        if (!constant.HasValue || constant.Value is not string name || !MessageSyntax.TryValidate(name, out _))
        {
            return null;
        }

        var category = StringLocalizerCategory(reference.Instance?.Type as INamedTypeSymbol, symbols.GenericStringLocalizer);
        IReadOnlyList<string> placeholders = [.. MessageSyntax.ExtractPlaceholders(name)];
        var site = new TranslationSite(name, name, null, category, null, placeholders, ToReference(node), null);
        return new TranslationSiteResult(site, []);
    }

    private static string StringLocalizerCategory(INamedTypeSymbol? instanceType, INamedTypeSymbol? genericLocalizer)
    {
        if (instanceType is null || genericLocalizer is null)
        {
            return string.Empty;
        }

        foreach (INamedTypeSymbol candidate in SelfBasesAndInterfaces(instanceType))
        {
            if (candidate.IsGenericType
                && SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, genericLocalizer)
                && candidate.TypeArguments.Length == 1
                && candidate.TypeArguments[0] is INamedTypeSymbol argument)
            {
                return FullName(argument);
            }
        }

        return string.Empty;
    }

    // The receiver whose type carries the [TranslationScope]: the instance for an ordinary call; the first
    // argument (the `this` parameter) for a reduced extension-method call, which has no Instance; and the
    // constructed type itself for an object-creation site. Missing any of these silently dropped the
    // category, so the call was extracted as global but resolved per-T at runtime.
    private static INamedTypeSymbol? ReceiverType(IOperation? operation) => operation switch
    {
        IInvocationOperation invocation => InvocationReceiver(invocation),
        IObjectCreationOperation creation => creation.Type as INamedTypeSymbol,
        _ => null
    };

    private static INamedTypeSymbol? InvocationReceiver(IInvocationOperation invocation)
    {
        if (invocation.Instance is not null)
        {
            return invocation.Instance.Type as INamedTypeSymbol;
        }

        return invocation.TargetMethod.IsExtensionMethod && invocation.Arguments.Length > 0
            ? invocation.Arguments[0].Value.Type as INamedTypeSymbol
            : null;
    }

    private static TranslationSiteResult Build(
        ImmutableArray<IArgumentOperation> arguments,
        IArgumentOperation keyArgument,
        AttributeSymbols symbols,
        SyntaxNode node,
        INamedTypeSymbol? receiver)
    {
        var problems = new List<DetectionProblem>();
        var key = Constant(keyArgument, problems);
        IArgumentOperation? defaultArgument = FindArgument(arguments, symbols.Default);
        var defaultMessage = Constant(defaultArgument, problems);
        var context = Constant(FindArgument(arguments, symbols.Context), problems);
        var comment = ResolveComment(arguments, symbols);

        if (key is null || defaultMessage is null || defaultArgument is null)
        {
            return new TranslationSiteResult(null, problems);
        }

        IReadOnlyList<string> placeholders = ResolvePlaceholders(defaultMessage, defaultArgument, problems);
        IReadOnlyList<string>? supplied = SuppliedArguments(arguments);
        AddArgumentProblems(placeholders, supplied, defaultArgument, problems);
        AddMissingOtherProblems(defaultMessage, defaultArgument, problems);

        var category = CategoryFrom(receiver, symbols.Scope);
        var site = new TranslationSite(key, defaultMessage, context, category, comment, placeholders, ToReference(node), supplied);
        return new TranslationSiteResult(site, problems);
    }

    // The category is the full name of the type argument bound to a [TranslationScope] type parameter on
    // the receiver's type (or any base type or interface) — so ILocalizer<T>, Localized<TSelf>, and any
    // user-rolled scoped localizer all resolve identically, matching the runtime. No marker → global.
    private static string CategoryFrom(INamedTypeSymbol? receiver, INamedTypeSymbol? scopeAttribute)
    {
        if (receiver is null || scopeAttribute is null)
        {
            return string.Empty;
        }

        foreach (INamedTypeSymbol candidate in SelfBasesAndInterfaces(receiver))
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            INamedTypeSymbol definition = candidate.OriginalDefinition;
            for (var index = 0; index < definition.TypeParameters.Length && index < candidate.TypeArguments.Length; index++)
            {
                if (HasScopeAttribute(definition.TypeParameters[index], scopeAttribute)
                    && candidate.TypeArguments[index] is INamedTypeSymbol argument)
                {
                    return FullName(argument);
                }
            }
        }

        return string.Empty;
    }

    private static IEnumerable<INamedTypeSymbol> SelfBasesAndInterfaces(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }

        foreach (INamedTypeSymbol contract in type.AllInterfaces)
        {
            yield return contract;
        }
    }

    private static bool HasScopeAttribute(ITypeParameterSymbol parameter, INamedTypeSymbol scopeAttribute)
    {
        foreach (AttributeData data in parameter.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(data.AttributeClass, scopeAttribute))
            {
                return true;
            }
        }

        return false;
    }

    private static string FullName(INamedTypeSymbol type)
    {
        var builder = new StringBuilder();
        if (type.ContainingType is not null)
        {
            builder.Append(FullName(type.ContainingType));
            builder.Append('+');
        }
        else if (type.ContainingNamespace is { IsGlobalNamespace: false })
        {
            builder.Append(type.ContainingNamespace.ToDisplayString());
            builder.Append('.');
        }

        builder.Append(type.Name);

        // Match Type.GetGenericTypeDefinition().FullName at runtime: a generic type carries its arity
        // backtick (e.g. Acme.Box`1) but not its type arguments, so the two categories agree.
        if (type.Arity > 0)
        {
            builder.Append('`').Append(type.Arity);
        }

        return builder.ToString();
    }

    private static void AddArgumentProblems(
        IReadOnlyList<string> placeholders,
        IReadOnlyList<string>? supplied,
        IArgumentOperation defaultArgument,
        List<DetectionProblem> problems)
    {
        if (supplied is null)
        {
            return;
        }

        Location location = defaultArgument.Value.Syntax.GetLocation();
        foreach (var placeholder in placeholders)
        {
            if (!supplied.Contains(placeholder))
            {
                problems.Add(new DetectionProblem(DetectionCause.PlaceholderNotSupplied, placeholder, location));
            }
        }

        foreach (var argument in supplied)
        {
            if (!placeholders.Contains(argument))
            {
                problems.Add(new DetectionProblem(DetectionCause.ArgumentNotUsed, argument, location));
            }
        }
    }

    private static void AddMissingOtherProblems(
        string defaultMessage,
        IArgumentOperation defaultArgument,
        List<DetectionProblem> problems)
    {
        Location location = defaultArgument.Value.Syntax.GetLocation();
        foreach (var argumentName in MessageSyntax.FindConstructsMissingOther(defaultMessage))
        {
            problems.Add(new DetectionProblem(DetectionCause.MissingOtherBranch, argumentName, location));
        }
    }

    private static IReadOnlyList<string>? SuppliedArguments(ImmutableArray<IArgumentOperation> arguments)
    {
        foreach (IArgumentOperation argument in arguments)
        {
            if (argument.Parameter is { IsParams: true })
            {
                return ExtractTupleNames(argument.Value);
            }
        }

        return null;
    }

    private static IReadOnlyList<string>? ExtractTupleNames(IOperation value)
    {
        if (value is not IArrayCreationOperation array || array.Initializer is null)
        {
            return null;
        }

        var names = new List<string>();
        foreach (IOperation element in array.Initializer.ElementValues)
        {
            var name = TupleFirstConstant(element);
            if (name is null)
            {
                return null;
            }

            names.Add(name);
        }

        return names;
    }

    private static string? TupleFirstConstant(IOperation element)
    {
        if (Unwrap(element) is ITupleOperation tuple && tuple.Elements.Length >= 1)
        {
            IOperation first = Unwrap(tuple.Elements[0]);
            if (first.ConstantValue.HasValue)
            {
                return first.ConstantValue.Value as string;
            }
        }

        return null;
    }

    private static IOperation Unwrap(IOperation operation) =>
        operation is IConversionOperation conversion ? conversion.Operand : operation;

    private static ImmutableArray<IArgumentOperation> ExtractArguments(IOperation? operation) => operation switch
    {
        IInvocationOperation invocation => invocation.Arguments,
        IObjectCreationOperation creation => creation.Arguments,
        _ => default
    };

    private static IArgumentOperation? FindArgument(ImmutableArray<IArgumentOperation> arguments, INamedTypeSymbol? attribute)
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (IArgumentOperation argument in arguments)
        {
            if (argument.Parameter is not null && HasAttribute(argument.Parameter, attribute))
            {
                return argument;
            }
        }

        return null;
    }

    private static bool HasAttribute(IParameterSymbol parameter, INamedTypeSymbol attribute)
    {
        foreach (AttributeData data in parameter.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Constant(IArgumentOperation? argument, List<DetectionProblem> problems)
    {
        if (argument is null)
        {
            return null;
        }

        Optional<object?> constant = argument.Value.ConstantValue;
        if (constant.HasValue)
        {
            return constant.Value as string;
        }

        problems.Add(new DetectionProblem(
            DetectionCause.NonConstantArgument,
            null,
            argument.Value.Syntax.GetLocation()));
        return null;
    }

    private static string? ResolveComment(ImmutableArray<IArgumentOperation> arguments, AttributeSymbols symbols)
    {
        IArgumentOperation? commentArgument = FindArgument(arguments, symbols.Comment);
        if (commentArgument is not null)
        {
            Optional<object?> constant = commentArgument.Value.ConstantValue;
            if (constant.HasValue)
            {
                return constant.Value as string;
            }
        }

        return MethodComment(arguments, symbols);
    }

    private static string? MethodComment(ImmutableArray<IArgumentOperation> arguments, AttributeSymbols symbols)
    {
        if (symbols.Comment is null || arguments.IsDefaultOrEmpty)
        {
            return null;
        }

        ISymbol? method = arguments[0].Parameter?.ContainingSymbol;
        if (method is null)
        {
            return null;
        }

        foreach (AttributeData data in method.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(data.AttributeClass, symbols.Comment)
                && data.ConstructorArguments.Length == 1)
            {
                return data.ConstructorArguments[0].Value as string;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ResolvePlaceholders(
        string defaultMessage,
        IArgumentOperation defaultArgument,
        List<DetectionProblem> problems)
    {
        if (!MessageSyntax.TryValidate(defaultMessage, out MessageFormatError? error))
        {
            problems.Add(new DetectionProblem(
                DetectionCause.InvalidMessageFormat,
                error!.Message,
                defaultArgument.Value.Syntax.GetLocation()));
            return [];
        }

        return [.. MessageSyntax.ExtractPlaceholders(defaultMessage)];
    }

    private static SourceReference ToReference(SyntaxNode node)
    {
        FileLinePositionSpan span = node.GetLocation().GetLineSpan();
        return new SourceReference(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
    }

    private sealed class AttributeSymbols
    {
        private AttributeSymbols(
            INamedTypeSymbol? translatable,
            INamedTypeSymbol? defaultMessage,
            INamedTypeSymbol? context,
            INamedTypeSymbol? comment,
            INamedTypeSymbol? scope,
            INamedTypeSymbol? stringLocalizer,
            INamedTypeSymbol? genericStringLocalizer)
        {
            Translatable = translatable;
            Default = defaultMessage;
            Context = context;
            Comment = comment;
            Scope = scope;
            StringLocalizer = stringLocalizer;
            GenericStringLocalizer = genericStringLocalizer;
        }

        public INamedTypeSymbol? Translatable { get; }

        public INamedTypeSymbol? Default { get; }

        public INamedTypeSymbol? Context { get; }

        public INamedTypeSymbol? Comment { get; }

        public INamedTypeSymbol? Scope { get; }

        public INamedTypeSymbol? StringLocalizer { get; }

        public INamedTypeSymbol? GenericStringLocalizer { get; }

        public static AttributeSymbols From(Compilation compilation) => new(
            compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.TranslatableAttribute"),
            compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.TranslationDefaultAttribute"),
            compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.TranslationContextAttribute"),
            compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.TranslationCommentAttribute"),
            compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.TranslationScopeAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.Extensions.Localization.IStringLocalizer"),
            compilation.GetTypeByMetadataName("Microsoft.Extensions.Localization.IStringLocalizer`1"));
    }
}
