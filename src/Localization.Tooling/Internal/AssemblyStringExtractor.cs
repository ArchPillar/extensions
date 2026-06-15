using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>A translatable call site recovered from compiled IL: the key, the in-code default, the category
/// (the localizer's type argument, empty for a global/non-generic localizer), and the optional disambiguation
/// context.</summary>
internal sealed record RawCallSite(string Key, string Default, string Category, string? Context);

/// <summary>
/// Recovers translatable call sites from a built assembly's IL, so extraction covers strings the source
/// generator never sees — Razor/<c>.cshtml</c> markup and any other source generator's output (Decision D-K).
/// It reads method bodies with Mono.Cecil (a tool-only dependency; nothing flows to a consumer's product) and
/// recognises sites by the same attribute contract the source-level detector uses: a call binding a constant
/// to a <c>[Translatable]</c> parameter, with the <c>[TranslationDefault]</c> parameter as the in-code default.
/// Reading the attributes off the resolved method — rather than hardcoding type or method names — means the
/// native <c>Translate</c>, the <c>loc["key", "default"]</c> indexer, the <c>L(...)</c> marker, and any user
/// wrapper are all found by one rule. The BCL <c>IStringLocalizer</c> indexer is the single exception, matched
/// by symbol because its fixed shape cannot carry the attributes. A lightweight evaluation-stack simulation
/// recovers the constant string each call consumes and the static type of the receiver (whose generic argument
/// is the category). Placeholder names are derived later from the ICU default, not the IL.
/// </summary>
internal sealed class AssemblyStringExtractor : IDisposable
{
    private const string StringLocalizerNamespace = "Microsoft.Extensions.Localization";
    private const string TranslatableAttribute = "ArchPillar.Extensions.Localization.TranslatableAttribute";
    private const string TranslationDefaultAttribute = "ArchPillar.Extensions.Localization.TranslationDefaultAttribute";
    private const string TranslationContextAttribute = "ArchPillar.Extensions.Localization.TranslationContextAttribute";
    private const string TranslationScopeAttribute = "ArchPillar.Extensions.Localization.TranslationScopeAttribute";
    private const string DisplayNameAttribute = "System.ComponentModel.DisplayNameAttribute";
    private const string DescriptionAttribute = "System.ComponentModel.DescriptionAttribute";
    private const string DisplayAttribute = "System.ComponentModel.DataAnnotations.DisplayAttribute";
    private const string LocalizedDisplayNameAttribute = "ArchPillar.Extensions.Localization.LocalizedDisplayNameAttribute";
    private const string LocalizedDescriptionAttribute = "ArchPillar.Extensions.Localization.LocalizedDescriptionAttribute";
    private const string LocalizedMessageAttribute = "ArchPillar.Extensions.Localization.LocalizedMessageAttribute`1";

    // One resolver and one method-binding cache for the whole batch: scanning a solution, the shared dependency
    // assemblies (ArchPillar.*, the framework) are loaded once rather than once per assembly, and a method called
    // across many assemblies (ILocalizer.Translate above all) is resolved once for the run. Both are keyed
    // globally — the resolver by assembly name, the cache by the method's declaring assembly + signature — so
    // nothing assembly-local leaks between scans.
    private readonly DefaultAssemblyResolver _resolver = new();
    private readonly Dictionary<string, Binding?> _bindings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _searchDirectories = new(StringComparer.OrdinalIgnoreCase);

    public AssemblyStringExtractor()
    {
        // The tool's own base directory carries the ArchPillar reference assemblies when running in-process.
        AddSearchDirectory(AppContext.BaseDirectory);
    }

    // A simulated evaluation-stack slot: the constant string it holds (from ldstr) and its static type (for a
    // receiver), either of which may be unknown.
    private readonly record struct Slot(string? Constant, TypeReference? Type);

    // Where a translatable method carries its key, default, and (optional, -1 when absent) context arguments.
    private readonly record struct Binding(int KeyIndex, int DefaultIndex, int ContextIndex);

    public IReadOnlyList<RawCallSite> Extract(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);

        // Resolving a call target to its definition (to read its parameter attributes) needs the referenced
        // ArchPillar assemblies, which sit beside the assembly being read in a real build output.
        AddSearchDirectory(Path.GetDirectoryName(fullPath)!);
        using var module = ModuleDefinition.ReadModule(fullPath, new ReaderParameters { AssemblyResolver = _resolver });

        // Early-out: an assembly that references neither localizer cannot contain a translatable call, so skip
        // reading its method bodies entirely — keeping a solution-wide scan cheap over unrelated assemblies.
        if (!module.AssemblyReferences.Any(reference =>
                reference.Name is "ArchPillar.Extensions.Localization" or "ArchPillar.Extensions.Localization.Abstractions"
                or "Microsoft.Extensions.Localization.Abstractions"))
        {
            return [];
        }

        var sites = new List<RawCallSite>();
        foreach (TypeDefinition type in AllTypes(module.Types))
        {
            foreach (MethodDefinition method in type.Methods)
            {
                if (method.HasBody)
                {
                    ScanMethod(method, sites, _bindings);
                }
            }
        }

        return sites;
    }

    /// <summary>
    /// Recovers translatable strings carried by display annotations on the assembly's own types — type,
    /// property, and field (enum member) <c>[DisplayName]</c> / <c>[Display]</c> / <c>[Description]</c>, plus
    /// the <c>[Localized…]</c> twins that override the key and default. Separate from <see cref="Extract"/>
    /// (which reads IL call sites) so a project can opt out of annotation extraction independently. The
    /// category is the declaring type's full name (a member's own enclosing type, a type's own name) — the
    /// reflection consumer's view, not the localizer-receiver view <see cref="Extract"/> uses.
    /// </summary>
    public IReadOnlyList<RawCallSite> ExtractAnnotations(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        AddSearchDirectory(Path.GetDirectoryName(fullPath)!);
        using var module = ModuleDefinition.ReadModule(fullPath, new ReaderParameters { AssemblyResolver = _resolver });

        var sites = new List<RawCallSite>();
        foreach (TypeDefinition type in AllTypes(module.Types))
        {
            var category = ReflectionName(type);
            AddAnnotationSites(type, category, sites);
            foreach (PropertyDefinition property in type.Properties)
            {
                AddAnnotationSites(property, category, sites);
            }

            foreach (FieldDefinition field in type.Fields)
            {
                AddAnnotationSites(field, category, sites);
            }
        }

        return sites;
    }

    // Cecil joins a nested type with '/', reflection's Type.FullName (which the runtime helper resolves the
    // category through) with '+'. Normalize here so an annotation on a nested type's member matches its lookup.
    private static string ReflectionName(TypeDefinition type) => type.FullName.Replace('/', '+');

    // Emits the display-name and description sites a member's attributes carry. A [Localized…] twin overrides
    // the system attribute for its concept (a stable key and a clean default); otherwise the system attribute's
    // literal is both key and default — the .resx-style text-as-key the framework already looks up by.
    private static void AddAnnotationSites(ICustomAttributeProvider member, string category, List<RawCallSite> sites)
    {
        // Display-name concept: [DisplayName("…")] or [Display(Name = "…")]. Description concept: [Description("…")]
        // or [Display(Description = "…")]. The two are distinct strings, so a member carrying both yields two sites.
        AddConcept(member, category, sites, LocalizedDisplayNameAttribute,
            LiteralFromConstructor(member, DisplayNameAttribute) ?? NamedArgument(member, DisplayAttribute, "Name"));
        AddConcept(member, category, sites, LocalizedDescriptionAttribute,
            LiteralFromConstructor(member, DescriptionAttribute) ?? NamedArgument(member, DisplayAttribute, "Description"));
        AddValidationMessageSites(member, category, sites);
    }

    // Emits one site for a display concept. A Localized twin attribute, which carries a stable key plus a clean
    // source default, takes precedence. Otherwise the system attribute's literal serves as both key and source
    // default, matching the resx-style text-as-key the framework looks up by. With neither present, no site.
    // Emits one site for a display concept. The system attribute's value is the key (the text-as-key default, or
    // a string id when the author prefers one); a Localized twin, when present, supplies the source default for
    // that key instead of reusing the key as the default. Nothing is emitted when there is no system value (no key).
    private static void AddConcept(ICustomAttributeProvider member, string category, List<RawCallSite> sites, string twinAttribute, string? systemKey)
    {
        if (systemKey is null)
        {
            return;
        }

        sites.Add(new RawCallSite(systemKey, LiteralFromConstructor(member, twinAttribute) ?? systemKey, category, Context: null));
    }

    // Emits a site per [LocalizedMessage<TValidation>] twin — a member may carry one per validator. The twin
    // supplies the source default; its key is the ErrorMessage of the validator named by the type argument, so the
    // catalog key matches what the framework looks the message up by. A twin whose validator sets no ErrorMessage
    // has no key, and is skipped.
    private static void AddValidationMessageSites(ICustomAttributeProvider member, string category, List<RawCallSite> sites)
    {
        foreach (CustomAttribute attribute in member.CustomAttributes)
        {
            if (attribute.AttributeType is GenericInstanceType generic
                && generic.ElementType.FullName == LocalizedMessageAttribute
                && attribute.ConstructorArguments.Count > 0
                && attribute.ConstructorArguments[0].Value is string defaultMessage
                && NamedArgument(member, generic.GenericArguments[0].FullName, "ErrorMessage") is { } key)
            {
                sites.Add(new RawCallSite(key, defaultMessage, category, Context: null));
            }
        }
    }

    // The first constructor-argument string of the named attribute on a member, or null when the attribute is
    // absent or carries no string argument — the [DisplayName("Email")] / [Description(...)] value, and equally a
    // [Localized…] twin's single constructor argument (its source default).
    private static string? LiteralFromConstructor(ICustomAttributeProvider member, string attributeFullName)
    {
        foreach (CustomAttribute attribute in member.CustomAttributes)
        {
            if (attribute.AttributeType.FullName == attributeFullName
                && attribute.ConstructorArguments.Count > 0
                && attribute.ConstructorArguments[0].Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    // The named-property string of the named attribute on a member, or null when the attribute is absent or the
    // property is unset (e.g. [Display(Name = "Email")] -> "Email").
    private static string? NamedArgument(ICustomAttributeProvider member, string attributeFullName, string propertyName)
    {
        foreach (CustomAttribute attribute in member.CustomAttributes)
        {
            if (attribute.AttributeType.FullName != attributeFullName)
            {
                continue;
            }

            foreach (CustomAttributeNamedArgument named in attribute.Properties)
            {
                if (named.Name == propertyName && named.Argument.Value is string value)
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose() => _resolver.Dispose();

    // Adds a probe directory to the shared resolver once, so repeated scans over the same output tree do not
    // pile up duplicate search paths.
    private void AddSearchDirectory(string directory)
    {
        if (_searchDirectories.Add(directory))
        {
            _resolver.AddSearchDirectory(directory);
        }
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nested in AllTypes(type.NestedTypes))
            {
                yield return nested;
            }
        }
    }

    private static void ScanMethod(MethodDefinition method, List<RawCallSite> sites, Dictionary<string, Binding?> bindings)
    {
        var stack = new List<Slot>();
        foreach (Instruction instruction in method.Body.Instructions)
        {
            OpCode op = instruction.OpCode;

            if (op == OpCodes.Ldstr)
            {
                stack.Add(new Slot((string)instruction.Operand, null));
                continue;
            }

            if (op.Code is Code.Call or Code.Callvirt && instruction.Operand is MethodReference target)
            {
                var argCount = target.Parameters.Count + (target.HasThis ? 1 : 0);
                IReadOnlyList<Slot> args = Pop(stack, argCount);
                Recognize(target, args, sites, bindings);
                if (target.ReturnType.FullName != "System.Void")
                {
                    stack.Add(new Slot(null, target.ReturnType));
                }

                continue;
            }

            if (op.Code == Code.Newobj && instruction.Operand is MethodReference constructor)
            {
                // newobj pops the constructor's parameters (not a receiver) and pushes the new instance — its
                // Varpop would otherwise be miscounted, desyncing the arg positions (e.g. a ValueTuple arg).
                Pop(stack, constructor.Parameters.Count);
                stack.Add(new Slot(null, constructor.DeclaringType));
                continue;
            }

            TypeReference? pushedType = PushedType(method, instruction);
            for (var i = 0; i < PopCount(op.StackBehaviourPop) && stack.Count > 0; i++)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            for (var i = 0; i < PushCount(op.StackBehaviourPush); i++)
            {
                stack.Add(new Slot(null, pushedType));
            }
        }
    }

    private static void Recognize(MethodReference target, IReadOnlyList<Slot> args, List<RawCallSite> sites, Dictionary<string, Binding?> bindings)
    {
        var receiver = target.HasThis ? 1 : 0; // arg 0 is the localizer instance for an instance call

        // The BCL IStringLocalizer indexer is a fixed shape we cannot annotate, so recognise it by symbol: the
        // looked-up name is both key and default (the .resx text-as-key convention). This is the one special
        // case — everything else goes through the attribute contract below.
        if (target.Name == "get_Item" && target.DeclaringType.Namespace == StringLocalizerNamespace
            && args.Count >= receiver + 1
            && args[receiver].Constant is { } name)
        {
            sites.Add(new RawCallSite(name, name, CategoryOf(args, receiver), Context: null));
            return;
        }

        // A recognised site always binds a constant key, so a call with no string-literal argument cannot be
        // one — a per-call-site check costing only a stack scan, no resolve.
        if (!HasConstantArgument(args))
        {
            return;
        }

        // Whether this method is translatable (and which parameters are the key/default/context) is resolved
        // once per distinct method and cached, so the resolve does not repeat across its many call sites.
        if (BindingFor(target, bindings) is not { } binding)
        {
            return;
        }

        if (ConstantAt(args, receiver, binding.KeyIndex) is { } key && ConstantAt(args, receiver, binding.DefaultIndex) is { } def)
        {
            var context = binding.ContextIndex >= 0 ? ConstantAt(args, receiver, binding.ContextIndex) : null;
            sites.Add(new RawCallSite(key, def, CategoryOf(args, receiver), context));
        }
    }

    // The translation binding for a distinct method, computed once and cached (null = not a translation method).
    // The key folds in the declaring assembly so two distinct methods that share a signature across assemblies
    // are never conflated, while the same method referenced from many assemblies maps to one entry.
    private static Binding? BindingFor(MethodReference target, Dictionary<string, Binding?> bindings)
    {
        var key = target.DeclaringType.Scope.Name + "/" + target.FullName;
        if (bindings.TryGetValue(key, out Binding? cached))
        {
            return cached;
        }

        Binding? binding = ComputeBinding(target);
        bindings[key] = binding;
        return binding;
    }

    // Decides translatability by the same rule the source-level detector applies: a parameter carrying
    // [Translatable] is the key and one carrying [TranslationDefault] is the in-code default. Reading those off
    // the resolved definition finds the native Translate, the loc["key", "default"] indexer, the L(...) marker
    // (one parameter carries both), and any user wrapper — without naming a single type or method. A translatable
    // method is never declared in the framework, so a System.* / Microsoft.* method is rejected by scope without
    // resolving (and thus loading) that assembly.
    private static Binding? ComputeBinding(MethodReference target)
    {
        if (IsFrameworkScope(target.DeclaringType.Scope))
        {
            return null;
        }

        MethodDefinition? definition = TryResolve(target);
        if (definition is null)
        {
            return null;
        }

        var keyIndex = IndexOfParameterWith(definition, TranslatableAttribute);
        var defaultIndex = IndexOfParameterWith(definition, TranslationDefaultAttribute);
        return keyIndex < 0 || defaultIndex < 0
            ? null
            : new Binding(keyIndex, defaultIndex, IndexOfParameterWith(definition, TranslationContextAttribute));
    }

    // The constant a parameter received at the call site: argument <paramref name="parameterIndex"/> sits after
    // the receiver on the simulated stack. Unknown (a non-constant argument) or out of range yields null.
    private static string? ConstantAt(IReadOnlyList<Slot> args, int receiver, int parameterIndex)
    {
        var position = receiver + parameterIndex;
        return position < args.Count ? args[position].Constant : null;
    }

    private static bool HasConstantArgument(IReadOnlyList<Slot> args)
    {
        foreach (Slot slot in args)
        {
            if (slot.Constant is not null)
            {
                return true;
            }
        }

        return false;
    }

    // The framework assemblies a translatable method is never declared in. Skipping them keeps the attribute
    // resolve from loading System.Private.CoreLib and the rest. The IStringLocalizer indexer is also a
    // Microsoft.* scope, but it is recognised by symbol before this gate, so excluding Microsoft.* costs nothing.
    private static bool IsFrameworkScope(IMetadataScope scope)
    {
        var name = scope.Name;
        return name is "mscorlib" or "netstandard" or "System" or "WindowsBase"
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    private static int IndexOfParameterWith(MethodDefinition method, string attributeFullName)
    {
        for (var index = 0; index < method.Parameters.Count; index++)
        {
            foreach (CustomAttribute attribute in method.Parameters[index].CustomAttributes)
            {
                if (attribute.AttributeType.FullName == attributeFullName)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    // Resolves a referenced method to its definition (which carries the parameter attributes). A reference into
    // an assembly that is not beside the target — or any other resolution failure — is simply not a known site.
    private static MethodDefinition? TryResolve(MethodReference reference)
    {
        try
        {
            return reference.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    // The category is the full name of the type argument bound to a [TranslationScope] type parameter on the
    // receiver's type, any of its base types, or its interfaces — so ILocalizer<T> (scope on the receiver) and
    // Localized<TSelf> (scope on a base) resolve identically to the runtime and the Roslyn detector, rather than
    // only the direct-receiver case. IStringLocalizer<T> is a framework type and carries no [TranslationScope],
    // so a generic receiver with no marker anywhere falls back to its own first type argument (its long-standing
    // behaviour). A non-generic receiver with no marked base (the concrete DefaultLocalizer, or an unknown type)
    // is the global category.
    private static string CategoryOf(IReadOnlyList<Slot> args, int receiver)
    {
        if (receiver <= 0 || args[0].Type is not { } receiverType)
        {
            return string.Empty;
        }

        foreach (GenericInstanceType candidate in SelfBasesAndInterfaces(receiverType))
        {
            var scoped = ScopedArgument(candidate);
            if (scoped is not null)
            {
                return scoped;
            }
        }

        return receiverType is GenericInstanceType { GenericArguments.Count: > 0 } generic
            ? generic.GenericArguments[0].FullName
            : string.Empty;
    }

    // The full name of the argument bound to a [TranslationScope] generic parameter of this constructed type, or
    // null when the definition cannot be resolved, has no such parameter, or the argument is itself an unresolved
    // type parameter (a scope flowing through an open generic — left to the Roslyn detector, which substitutes).
    private static string? ScopedArgument(GenericInstanceType constructed)
    {
        TypeDefinition? definition = TryResolveType(constructed);
        if (definition is null)
        {
            return null;
        }

        for (var index = 0; index < definition.GenericParameters.Count && index < constructed.GenericArguments.Count; index++)
        {
            TypeReference argument = constructed.GenericArguments[index];
            if (HasScopeAttribute(definition.GenericParameters[index]) && argument is not GenericParameter)
            {
                return argument.FullName;
            }
        }

        return null;
    }

    private static bool HasScopeAttribute(GenericParameter parameter)
    {
        foreach (CustomAttribute attribute in parameter.CustomAttributes)
        {
            if (attribute.AttributeType.FullName == TranslationScopeAttribute)
            {
                return true;
            }
        }

        return false;
    }

    // The receiver type, then each base type up the chain, then the interfaces seen along the way — each yielded
    // where it is a constructed generic type, so a [TranslationScope] argument can be read off it. Mirrors the
    // Roslyn detector's self-bases-and-interfaces walk; resolution failure simply ends the walk.
    private static IEnumerable<GenericInstanceType> SelfBasesAndInterfaces(TypeReference type)
    {
        TypeReference? current = type;
        while (current is not null)
        {
            if (current is GenericInstanceType generic)
            {
                yield return generic;
            }

            TypeDefinition? definition = TryResolveType(current);
            if (definition is null)
            {
                yield break;
            }

            foreach (InterfaceImplementation implementation in definition.Interfaces)
            {
                if (implementation.InterfaceType is GenericInstanceType genericInterface)
                {
                    yield return genericInterface;
                }
            }

            current = definition.BaseType;
        }
    }

    private static TypeDefinition? TryResolveType(TypeReference reference)
    {
        try
        {
            return reference.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    // Pops the top <paramref name="count"/> slots and returns them in argument order (deepest = arg 0).
    private static IReadOnlyList<Slot> Pop(List<Slot> stack, int count)
    {
        var taken = Math.Min(count, stack.Count);
        var start = stack.Count - taken;
        List<Slot> args = stack.GetRange(start, taken);
        stack.RemoveRange(start, taken);
        // A stack underflow (an arg produced across a branch we did not model) pads with unknowns so the fixed
        // argument positions still line up.
        return count > taken ? [.. Enumerable.Repeat(default(Slot), count - taken), .. args] : args;
    }

    // The static type a value-producing instruction pushes, where it is a likely receiver source (a parameter,
    // local, or field). Other instructions push an unknown type.
    private static TypeReference? PushedType(MethodDefinition method, Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Ldarg_0 => ArgType(method, 0),
        Code.Ldarg_1 => ArgType(method, 1),
        Code.Ldarg_2 => ArgType(method, 2),
        Code.Ldarg_3 => ArgType(method, 3),
        Code.Ldarg or Code.Ldarg_S => ((ParameterDefinition)instruction.Operand).ParameterType,
        Code.Ldloc_0 => LocalType(method, 0),
        Code.Ldloc_1 => LocalType(method, 1),
        Code.Ldloc_2 => LocalType(method, 2),
        Code.Ldloc_3 => LocalType(method, 3),
        Code.Ldloc or Code.Ldloc_S => ((VariableDefinition)instruction.Operand).VariableType,
        Code.Ldfld or Code.Ldsfld => ((FieldReference)instruction.Operand).FieldType,
        _ => null
    };

    private static TypeReference? ArgType(MethodDefinition method, int slot)
    {
        if (method.HasThis)
        {
            if (slot == 0)
            {
                return method.DeclaringType;
            }

            slot--;
        }

        return slot < method.Parameters.Count ? method.Parameters[slot].ParameterType : null;
    }

    private static TypeReference? LocalType(MethodDefinition method, int slot) =>
        slot < method.Body.Variables.Count ? method.Body.Variables[slot].VariableType : null;

    private static int PopCount(StackBehaviour pop) => pop switch
    {
        StackBehaviour.Pop0 => 0,
        StackBehaviour.Varpop or StackBehaviour.PopAll => 0,
        StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
            or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1
            or StackBehaviour.Popref_popi => 2,
        _ => 3
    };

    private static int PushCount(StackBehaviour push) => push switch
    {
        StackBehaviour.Push0 or StackBehaviour.Varpush => 0,
        StackBehaviour.Push1_push1 => 2,
        _ => 1
    };
}
