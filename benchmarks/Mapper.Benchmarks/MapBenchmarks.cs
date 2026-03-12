using BenchmarkDotNet.Attributes;

namespace ArchPillar.Mapper.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class MapBenchmarks
{
    private BenchmarkMappers _mappers = null!;

    // Sources — pre-allocated so Arrange cost is excluded
    private EmptySource               _emptySource = null!;
    private SinglePropSource          _singlePropSource = null!;
    private FivePropSource            _fivePropSource = null!;
    private TenPropSource             _tenPropSource = null!;
    private ListCollectionSource      _listCollectionSource = null!;
    private ArrayCollectionSource     _arrayCollectionSource = null!;
    private HashSetCollectionSource   _hashSetCollectionSource = null!;
    private DictionaryCollectionSource _dictionaryCollectionSource = null!;
    private NestedEmptySource         _nestedEmptySource = null!;
    private N5L1                      _nested5Source = null!;
    private N10L1                     _nested10Source = null!;

    [GlobalSetup]
    public void Setup()
    {
        _mappers = new BenchmarkMappers();

        _emptySource = new EmptySource();

        _singlePropSource = new SinglePropSource { Name = "Alice" };

        _fivePropSource = new FivePropSource
        {
            Name = "Alice", Age = 30, Email = "a@b.com", City = "NYC", Active = true,
        };

        _tenPropSource = new TenPropSource
        {
            P1 = "a", P2 = "b", P3 = "c", P4 = "d", P5 = "e",
            P6 = 1, P7 = 2, P8 = 3, P9 = 4, P10 = 5,
        };

        _listCollectionSource = new ListCollectionSource
        {
            Items = [new EmptySource()],
        };
        _arrayCollectionSource = new ArrayCollectionSource
        {
            Items = [new EmptySource()],
        };
        _hashSetCollectionSource = new HashSetCollectionSource
        {
            Items = [new EmptySource()],
        };
        _dictionaryCollectionSource = new DictionaryCollectionSource
        {
            Items = [new KeyedSource { Key = 1 }],
        };

        _nestedEmptySource = new NestedEmptySource
        {
            Child = new NestedEmptyChild(),
        };

        _nested5Source = new N5L1 { V = "1", Child = new N5L2 { V = "2", Child = new N5L3 { V = "3", Child = new N5L4 { V = "4", Child = new N5L5 { V = "5" } } } } };

        _nested10Source = new N10L1 { V = "1", Child = new N10L2 { V = "2", Child = new N10L3 { V = "3", Child = new N10L4 { V = "4", Child = new N10L5 { V = "5", Child = new N10L6 { V = "6", Child = new N10L7 { V = "7", Child = new N10L8 { V = "8", Child = new N10L9 { V = "9", Child = new N10L10 { V = "10" } } } } } } } } } };
    }

    // -------------------------------------------------------------------------
    // Scalar benchmarks
    // -------------------------------------------------------------------------

    [Benchmark(Description = "Empty→Empty")]
    public EmptyDest Map_Empty() => _mappers.Empty.Map(_emptySource)!;

    [Benchmark(Description = "1 property")]
    public SinglePropDest Map_SingleProp() => _mappers.SingleProp.Map(_singlePropSource)!;

    [Benchmark(Description = "5 properties")]
    public FivePropDest Map_FiveProp() => _mappers.FiveProp.Map(_fivePropSource)!;

    [Benchmark(Description = "10 properties")]
    public TenPropDest Map_TenProp() => _mappers.TenProp.Map(_tenPropSource)!;

    // -------------------------------------------------------------------------
    // Collection benchmarks
    // -------------------------------------------------------------------------

    [Benchmark(Description = "List<T> (1 item)")]
    public ListCollectionDest Map_ListCollection() => _mappers.ListCollection.Map(_listCollectionSource)!;

    [Benchmark(Description = "T[] (1 item)")]
    public ArrayCollectionDest Map_ArrayCollection() => _mappers.ArrayCollection.Map(_arrayCollectionSource)!;

    [Benchmark(Description = "HashSet<T> (1 item)")]
    public HashSetCollectionDest Map_HashSetCollection() => _mappers.HashSetCollection.Map(_hashSetCollectionSource)!;

    [Benchmark(Description = "Dictionary<K,V> (1 item)")]
    public DictionaryCollectionDest Map_DictionaryCollection() => _mappers.DictionaryCollection.Map(_dictionaryCollectionSource)!;

    // -------------------------------------------------------------------------
    // Baselines — raw LINQ (no mapper) for comparison
    // -------------------------------------------------------------------------

    [Benchmark(Description = "Baseline: Select+ToList (1 item)")]
    public List<EmptyDest> Baseline_SelectToList() => _listCollectionSource.Items.Select(_ => new EmptyDest()).ToList();

    [Benchmark(Description = "Baseline: Select+ToArray (1 item)")]
    public EmptyDest[] Baseline_SelectToArray() => _arrayCollectionSource.Items.Select(_ => new EmptyDest()).ToArray();

    [Benchmark(Description = "Baseline: Select+ToHashSet (1 item)")]
    public HashSet<EmptyDest> Baseline_SelectToHashSet() => _hashSetCollectionSource.Items.Select(_ => new EmptyDest()).ToHashSet();

    [Benchmark(Description = "Baseline: new List (1 item)")]
    public List<EmptyDest> Baseline_NewList() => [new EmptyDest()];

    // -------------------------------------------------------------------------
    // Nesting benchmarks
    // -------------------------------------------------------------------------

    [Benchmark(Description = "Nested (empty objects)")]
    public NestedEmptyDest Map_NestedEmpty() => _mappers.NestedEmpty.Map(_nestedEmptySource)!;

    [Benchmark(Description = "5-level nesting")]
    public N5L1Dto Map_Nested5() => _mappers.Nested5.Map(_nested5Source)!;

    [Benchmark(Description = "10-level nesting")]
    public N10L1Dto Map_Nested10() => _mappers.Nested10.Map(_nested10Source)!;
}

// =============================================================================
// Benchmark mapper context
// =============================================================================

public class BenchmarkMappers : MapperContext
{
    // Scalar
    public Mapper<EmptySource, EmptyDest>          Empty { get; }
    public Mapper<SinglePropSource, SinglePropDest> SingleProp { get; }
    public Mapper<FivePropSource, FivePropDest>     FiveProp { get; }
    public Mapper<TenPropSource, TenPropDest>       TenProp { get; }

    // Collections
    public Mapper<EmptySource, EmptyDest>                              EmptyItem { get; }
    public Mapper<ListCollectionSource, ListCollectionDest>            ListCollection { get; }
    public Mapper<ArrayCollectionSource, ArrayCollectionDest>          ArrayCollection { get; }
    public Mapper<HashSetCollectionSource, HashSetCollectionDest>      HashSetCollection { get; }
    public Mapper<KeyedSource, KeyedDest>                              KeyedItem { get; }
    public Mapper<DictionaryCollectionSource, DictionaryCollectionDest> DictionaryCollection { get; }

    // Nesting
    public Mapper<NestedEmptyChild, NestedEmptyChildDto> NestedEmptyChild { get; }
    public Mapper<NestedEmptySource, NestedEmptyDest>    NestedEmpty { get; }

    // 5-level
    public Mapper<N5L5, N5L5Dto> N5L5 { get; }
    public Mapper<N5L4, N5L4Dto> N5L4 { get; }
    public Mapper<N5L3, N5L3Dto> N5L3 { get; }
    public Mapper<N5L2, N5L2Dto> N5L2 { get; }
    public Mapper<N5L1, N5L1Dto> Nested5 { get; }

    // 10-level
    public Mapper<N10L10, N10L10Dto> N10L10 { get; }
    public Mapper<N10L9, N10L9Dto>   N10L9 { get; }
    public Mapper<N10L8, N10L8Dto>   N10L8 { get; }
    public Mapper<N10L7, N10L7Dto>   N10L7 { get; }
    public Mapper<N10L6, N10L6Dto>   N10L6 { get; }
    public Mapper<N10L5, N10L5Dto>   N10L5 { get; }
    public Mapper<N10L4, N10L4Dto>   N10L4 { get; }
    public Mapper<N10L3, N10L3Dto>   N10L3 { get; }
    public Mapper<N10L2, N10L2Dto>   N10L2 { get; }
    public Mapper<N10L1, N10L1Dto>   Nested10 { get; }

    public BenchmarkMappers()
    {
        // --- Scalar ---
        Empty = CreateMapper<EmptySource, EmptyDest>(_ => new EmptyDest());

        SingleProp = CreateMapper<SinglePropSource, SinglePropDest>(s => new SinglePropDest
        {
            Name = s.Name,
        });

        FiveProp = CreateMapper<FivePropSource, FivePropDest>(s => new FivePropDest
        {
            Name = s.Name, Age = s.Age, Email = s.Email, City = s.City, Active = s.Active,
        });

        TenProp = CreateMapper<TenPropSource, TenPropDest>(s => new TenPropDest
        {
            P1 = s.P1, P2 = s.P2, P3 = s.P3, P4 = s.P4, P5 = s.P5,
            P6 = s.P6, P7 = s.P7, P8 = s.P8, P9 = s.P9, P10 = s.P10,
        });

        // --- Collections ---
        EmptyItem = CreateMapper<EmptySource, EmptyDest>(_ => new EmptyDest());

        ListCollection = CreateMapper<ListCollectionSource, ListCollectionDest>(s => new ListCollectionDest
        {
            Items = s.Items.Project(EmptyItem).ToList(),
        });

        ArrayCollection = CreateMapper<ArrayCollectionSource, ArrayCollectionDest>(s => new ArrayCollectionDest
        {
            Items = s.Items.Project(EmptyItem).ToArray(),
        });

        HashSetCollection = CreateMapper<HashSetCollectionSource, HashSetCollectionDest>(s => new HashSetCollectionDest
        {
            Items = s.Items.Project(EmptyItem).ToHashSet(),
        });

        KeyedItem = CreateMapper<KeyedSource, KeyedDest>(s => new KeyedDest
        {
            Key = s.Key,
        });

        DictionaryCollection = CreateMapper<DictionaryCollectionSource, DictionaryCollectionDest>(s => new DictionaryCollectionDest
        {
            Items = s.Items.ToDictionary(i => i.Key, i => new KeyedDest { Key = i.Key }),
        });

        // --- Nested empty ---
        NestedEmptyChild = CreateMapper<NestedEmptyChild, NestedEmptyChildDto>(_ => new NestedEmptyChildDto());

        NestedEmpty = CreateMapper<NestedEmptySource, NestedEmptyDest>(s => new NestedEmptyDest
        {
            Child = NestedEmptyChild.Map(s.Child),
        });

        // --- 5-level nesting ---
        N5L5 = CreateMapper<N5L5, N5L5Dto>(s => new N5L5Dto { V = s.V });
        N5L4 = CreateMapper<N5L4, N5L4Dto>(s => new N5L4Dto { V = s.V, Child = N5L5.Map(s.Child) });
        N5L3 = CreateMapper<N5L3, N5L3Dto>(s => new N5L3Dto { V = s.V, Child = N5L4.Map(s.Child) });
        N5L2 = CreateMapper<N5L2, N5L2Dto>(s => new N5L2Dto { V = s.V, Child = N5L3.Map(s.Child) });
        Nested5 = CreateMapper<N5L1, N5L1Dto>(s => new N5L1Dto { V = s.V, Child = N5L2.Map(s.Child) });

        // --- 10-level nesting ---
        N10L10 = CreateMapper<N10L10, N10L10Dto>(s => new N10L10Dto { V = s.V });
        N10L9 = CreateMapper<N10L9, N10L9Dto>(s => new N10L9Dto { V = s.V, Child = N10L10.Map(s.Child) });
        N10L8 = CreateMapper<N10L8, N10L8Dto>(s => new N10L8Dto { V = s.V, Child = N10L9.Map(s.Child) });
        N10L7 = CreateMapper<N10L7, N10L7Dto>(s => new N10L7Dto { V = s.V, Child = N10L8.Map(s.Child) });
        N10L6 = CreateMapper<N10L6, N10L6Dto>(s => new N10L6Dto { V = s.V, Child = N10L7.Map(s.Child) });
        N10L5 = CreateMapper<N10L5, N10L5Dto>(s => new N10L5Dto { V = s.V, Child = N10L6.Map(s.Child) });
        N10L4 = CreateMapper<N10L4, N10L4Dto>(s => new N10L4Dto { V = s.V, Child = N10L5.Map(s.Child) });
        N10L3 = CreateMapper<N10L3, N10L3Dto>(s => new N10L3Dto { V = s.V, Child = N10L4.Map(s.Child) });
        N10L2 = CreateMapper<N10L2, N10L2Dto>(s => new N10L2Dto { V = s.V, Child = N10L3.Map(s.Child) });
        Nested10 = CreateMapper<N10L1, N10L1Dto>(s => new N10L1Dto { V = s.V, Child = N10L2.Map(s.Child) });

        EagerBuildAll();
    }
}

// =============================================================================
// Benchmark models
// =============================================================================

// --- Empty ---
public class EmptySource { }
public class EmptyDest   { }

// --- Single property ---
public class SinglePropSource { public required string Name { get; set; } }
public class SinglePropDest   { public required string Name { get; set; } }

// --- Five properties ---
public class FivePropSource
{
    public required string Name   { get; set; }
    public required int    Age    { get; set; }
    public required string Email  { get; set; }
    public required string City   { get; set; }
    public required bool   Active { get; set; }
}

public class FivePropDest
{
    public required string Name   { get; set; }
    public required int    Age    { get; set; }
    public required string Email  { get; set; }
    public required string City   { get; set; }
    public required bool   Active { get; set; }
}

// --- Ten properties ---
public class TenPropSource
{
    public required string P1  { get; set; }
    public required string P2  { get; set; }
    public required string P3  { get; set; }
    public required string P4  { get; set; }
    public required string P5  { get; set; }
    public required int    P6  { get; set; }
    public required int    P7  { get; set; }
    public required int    P8  { get; set; }
    public required int    P9  { get; set; }
    public required int    P10 { get; set; }
}

public class TenPropDest
{
    public required string P1  { get; set; }
    public required string P2  { get; set; }
    public required string P3  { get; set; }
    public required string P4  { get; set; }
    public required string P5  { get; set; }
    public required int    P6  { get; set; }
    public required int    P7  { get; set; }
    public required int    P8  { get; set; }
    public required int    P9  { get; set; }
    public required int    P10 { get; set; }
}

// --- Collections ---
public class ListCollectionSource    { public required List<EmptySource> Items { get; set; } }
public class ListCollectionDest      { public required List<EmptyDest>   Items { get; set; } }

public class ArrayCollectionSource   { public required EmptySource[]     Items { get; set; } }
public class ArrayCollectionDest     { public required EmptyDest[]       Items { get; set; } }

public class HashSetCollectionSource { public required HashSet<EmptySource> Items { get; set; } }
public class HashSetCollectionDest   { public required HashSet<EmptyDest>   Items { get; set; } }

public class KeyedSource { public required int Key { get; set; } }
public class KeyedDest   { public required int Key { get; set; } }

public class DictionaryCollectionSource { public required List<KeyedSource>            Items { get; set; } }
public class DictionaryCollectionDest   { public required Dictionary<int, KeyedDest>   Items { get; set; } }

// --- Nested empty ---
public class NestedEmptyChild    { }
public class NestedEmptyChildDto { }
public class NestedEmptySource   { public required NestedEmptyChild    Child { get; set; } }
public class NestedEmptyDest     { public required NestedEmptyChildDto Child { get; set; } }

// --- 5-level nesting (each level has one string property) ---
public class N5L5    { public required string V { get; set; } }
public class N5L5Dto { public required string V { get; set; } }

public class N5L4    { public required string V { get; set; } public required N5L5    Child { get; set; } }
public class N5L4Dto { public required string V { get; set; } public required N5L5Dto Child { get; set; } }

public class N5L3    { public required string V { get; set; } public required N5L4    Child { get; set; } }
public class N5L3Dto { public required string V { get; set; } public required N5L4Dto Child { get; set; } }

public class N5L2    { public required string V { get; set; } public required N5L3    Child { get; set; } }
public class N5L2Dto { public required string V { get; set; } public required N5L3Dto Child { get; set; } }

public class N5L1    { public required string V { get; set; } public required N5L2    Child { get; set; } }
public class N5L1Dto { public required string V { get; set; } public required N5L2Dto Child { get; set; } }

// --- 10-level nesting (each level has one string property) ---
public class N10L10    { public required string V { get; set; } }
public class N10L10Dto { public required string V { get; set; } }

public class N10L9    { public required string V { get; set; } public required N10L10   Child { get; set; } }
public class N10L9Dto { public required string V { get; set; } public required N10L10Dto Child { get; set; } }

public class N10L8    { public required string V { get; set; } public required N10L9    Child { get; set; } }
public class N10L8Dto { public required string V { get; set; } public required N10L9Dto Child { get; set; } }

public class N10L7    { public required string V { get; set; } public required N10L8    Child { get; set; } }
public class N10L7Dto { public required string V { get; set; } public required N10L8Dto Child { get; set; } }

public class N10L6    { public required string V { get; set; } public required N10L7    Child { get; set; } }
public class N10L6Dto { public required string V { get; set; } public required N10L7Dto Child { get; set; } }

public class N10L5    { public required string V { get; set; } public required N10L6    Child { get; set; } }
public class N10L5Dto { public required string V { get; set; } public required N10L6Dto Child { get; set; } }

public class N10L4    { public required string V { get; set; } public required N10L5    Child { get; set; } }
public class N10L4Dto { public required string V { get; set; } public required N10L5Dto Child { get; set; } }

public class N10L3    { public required string V { get; set; } public required N10L4    Child { get; set; } }
public class N10L3Dto { public required string V { get; set; } public required N10L4Dto Child { get; set; } }

public class N10L2    { public required string V { get; set; } public required N10L3    Child { get; set; } }
public class N10L2Dto { public required string V { get; set; } public required N10L3Dto Child { get; set; } }

public class N10L1    { public required string V { get; set; } public required N10L2    Child { get; set; } }
public class N10L1Dto { public required string V { get; set; } public required N10L2Dto Child { get; set; } }
