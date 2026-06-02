namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Holds a nested mapper's already-compiled mapping delegate so an
/// <see cref="Mapper{TSource,TDest}.Invoke(TSource)"/> call can be evaluated in
/// memory from inside a LINQ projection.
/// <para>
/// The box's type is deliberately <em>not</em> a mapper type. EF Core's
/// mapper-pinning evaluatable filter therefore ignores it, and the funcletizer
/// lifts <see cref="Map"/> into a query parameter instead of leaving the mapper
/// as a captured constant the client-projection verifier would reject. The
/// delegate is the mapper's existing cached delegate (its <c>Invoke</c>/<c>Map</c>
/// method group) — the mapper's expression is never re-extracted or recompiled.
/// </para>
/// </summary>
internal sealed class MapperInvokeBox<TSource, TResult>
{
    public MapperInvokeBox(Func<TSource, TResult> map)
    {
        Map = map;
    }

    public Func<TSource, TResult> Map { get; }
}
