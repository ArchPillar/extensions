#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices;

/// <summary>
/// Enables <c>init</c>-only setters and positional records when targeting
/// <c>netstandard2.0</c>, where this type is absent from the Base Class Library.
/// </summary>
internal static class IsExternalInit
{
}
#endif
