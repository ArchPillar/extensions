namespace ArchPillar.Mapper.Tests;

/// <summary>
/// Verifies that the builder's dictionary-based deduplication produces
/// last-wins semantics: a fluent .Map() call overrides a member-init
/// binding for the same destination property.
/// </summary>
public class BuilderDeduplicationTests
{
    [Fact]
    public void Build_FluentMapOverridesMemberInit_LastWins()
    {
        var context = new DeduplicationContext();
        var src = new DeduplicationSource { FirstName = "Alice", LastName = "Smith" };

        DeduplicationDest? dto = context.Mapper.Map(src);

        // The fluent .Map() overrides the member-init for FullName
        Assert.Equal("Smith, Alice", dto!.FullName);
    }

    [Fact]
    public void Build_MultipleFluent_LastWins()
    {
        var context = new DeduplicationContext();
        var src = new DeduplicationSource { FirstName = "Alice", LastName = "Smith" };

        DeduplicationDest? dto = context.DoubleOverrideMapper.Map(src);

        // Second .Map() overrides the first
        Assert.Equal("ALICE", dto!.FullName);
    }

    [Fact]
    public void Build_NullableValueType_AutoIgnored()
    {
        // A nullable value-type property (int?) should not require explicit mapping
        var context = new NullableValueTypeContext();

        Exception? exception = Record.Exception(() => context.Build());
        Assert.Null(exception);
    }

    [Fact]
    public void Map_NullableValueType_DefaultsToNull()
    {
        var context = new NullableValueTypeContext();
        var src = new NullableSource { Name = "Test" };

        NullableDest? dto = context.Build().Map(src);

        Assert.Equal("Test", dto!.Name);
        Assert.Null(dto.Score);
    }
}

// ---------------------------------------------------------------------------
// Test-local models and mappers
// ---------------------------------------------------------------------------

file class DeduplicationSource
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
}

file class DeduplicationDest
{
    public required string FullName { get; set; }
}

file class DeduplicationContext : MapperContext
{
    public Mapper<DeduplicationSource, DeduplicationDest> Mapper { get; }
    public Mapper<DeduplicationSource, DeduplicationDest> DoubleOverrideMapper { get; }

    public DeduplicationContext()
    {
        // Member-init sets FullName = "FirstName LastName"
        // Fluent .Map() overrides it to "LastName, FirstName"
        Mapper = CreateMapper<DeduplicationSource, DeduplicationDest>(s => new DeduplicationDest
        {
            FullName = s.FirstName + " " + s.LastName,
        })
        .Map(d => d.FullName, s => s.LastName + ", " + s.FirstName);

        // Two fluent .Map() calls — second one wins
        DoubleOverrideMapper = CreateMapper<DeduplicationSource, DeduplicationDest>()
            .Map(d => d.FullName, s => s.FirstName + " " + s.LastName)
            .Map(d => d.FullName, s => s.FirstName.ToUpper());
    }
}

file class NullableSource
{
    public required string Name { get; set; }
    public int? Score { get; set; }
}

file class NullableDest
{
    public required string Name { get; set; }
    public int? Score { get; set; }
}

file class NullableValueTypeContext : MapperContext
{
    public Mapper<NullableSource, NullableDest> Build() =>
        CreateMapper<NullableSource, NullableDest>(s => new NullableDest
        {
            Name = s.Name,
            // Score is int? — should be auto-ignored by RequiresCoverage
        });
}
