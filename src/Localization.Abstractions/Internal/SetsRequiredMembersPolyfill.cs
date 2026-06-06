#if NETSTANDARD2_0
namespace System.Diagnostics.CodeAnalysis;

/// <summary>Indicates that a constructor sets all <c>required</c> members, on <c>netstandard2.0</c>.</summary>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
internal sealed class SetsRequiredMembersAttribute : Attribute
{
}
#endif
