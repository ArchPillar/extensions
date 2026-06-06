#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices;

/// <summary>Enables <c>init</c>-only setters and records on <c>netstandard2.0</c>.</summary>
internal static class IsExternalInit
{
}

/// <summary>Supports the <c>required</c> modifier on <c>netstandard2.0</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
internal sealed class RequiredMemberAttribute : Attribute
{
}

/// <summary>Supports compiler-required features (such as <c>required</c>) on <c>netstandard2.0</c>.</summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
internal sealed class CompilerFeatureRequiredAttribute : Attribute
{
    public CompilerFeatureRequiredAttribute(string featureName)
    {
        FeatureName = featureName;
    }

    public string FeatureName { get; }

    public bool IsOptional { get; init; }
}
#endif
