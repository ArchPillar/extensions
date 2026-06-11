namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies that <see cref="MapperBuilder{TSource,TDest}.Build"/> rejects a
/// mapping whose source value cannot be assigned to its destination member.
/// <para>
/// Type inference on <c>Map</c> / <c>Optional</c> unifies the value type to the
/// most general common type, so a source that produces a <em>base</em> type of a
/// more-derived destination property (most commonly a nested mapper that targets
/// a base DTO) is accepted by the compiler but cannot bind. The builder must
/// surface this at build time rather than deferring to the first compile or
/// projection.
/// </para>
/// </summary>
public sealed class BuildTimeTypeValidationTests
{
    [Fact]
    public void Build_MapSourceWiderThanDestination_ThrowsClearError()
    {
        var context = new WideningContext();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => context.BuildMapWidened());

        Assert.Contains(nameof(OwnerDto.Pet), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DogDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AnimalDto), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OptionalSourceWiderThanDestination_ThrowsClearError()
    {
        var context = new WideningContext();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => context.BuildOptionalWidened());

        Assert.Contains(nameof(OwnerDto.Pet), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NestedMapperTargetingBaseType_ThrowsClearError()
    {
        // The realistic trigger: a nested mapper that maps to the base DTO is
        // wired into a derived destination property. Inference widens to the
        // base DTO; the compiler accepts it but the value cannot bind.
        var context = new WideningContext();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => context.BuildNestedBaseMapper());

        Assert.Contains(nameof(OwnerDto.Pet), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DogDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AnimalDto), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NestedMapperTargetingExactType_DoesNotThrow()
    {
        var context = new WideningContext();

        Exception? exception = Record.Exception(() => context.BuildNestedExactMapper());
        Assert.Null(exception);
    }

    [Fact]
    public void Build_ExactDestinationType_DoesNotThrow()
    {
        var context = new WideningContext();

        Exception? exception = Record.Exception(() => context.BuildExact());
        Assert.Null(exception);
    }

    [Fact]
    public void Build_SourceAssignableToBaseDestination_DoesNotThrow()
    {
        // Mapping a derived source value to a base destination property is valid
        // (the value IS-A the destination type) and must keep building.
        var context = new WideningContext();

        Exception? exception = Record.Exception(() => context.BuildAssignableToBase());
        Assert.Null(exception);
    }

    [Fact]
    public void Build_ImplicitNumericWidening_DoesNotThrow()
    {
        // int -> long carries a Convert node from inference, so the source body
        // type already matches the destination. It must not be flagged.
        var context = new WideningContext();

        Exception? exception = Record.Exception(() => context.BuildNumericWidening());
        Assert.Null(exception);
    }
}

// ---------------------------------------------------------------------------
// Models — a base/derived DTO pair where inference can widen to the base type
// ---------------------------------------------------------------------------

file sealed class Animal
{
    public string Name { get; set; } = "";
}

file sealed class Dog
{
    public string Name { get; set; } = "";
    public string Breed { get; set; } = "";
}

file class AnimalDto
{
    public string Name { get; set; } = "";
}

file sealed class DogDto : AnimalDto
{
    public string Breed { get; set; } = "";
}

file sealed class Owner
{
    public Animal Beast { get; set; } = new();
    public Dog Pet { get; set; } = new();
    public DogDto AsDog { get; set; } = new();
    public AnimalDto AsAnimal { get; set; } = new();
}

file sealed class OwnerDto
{
    public DogDto Pet { get; set; } = new();
}

// Destination property typed as the base DTO — accepts a derived source value.
file sealed class BaseHolderDto
{
    public AnimalDto Pet { get; set; } = new();
}

file sealed class NumberSource
{
    public int Value { get; set; }
}

file sealed class NumberDest
{
    public long Value { get; set; }
}

file sealed class WideningContext : MapperContext
{
    public Mapper<Animal, AnimalDto> AnimalMapper { get; }
    public Mapper<Dog, DogDto> DogMapper { get; }

    public WideningContext()
    {
        AnimalMapper = CreateMapper<Animal, AnimalDto>(s => new AnimalDto { Name = s.Name });
        DogMapper    = CreateMapper<Dog, DogDto>(s => new DogDto { Name = s.Name, Breed = s.Breed });
    }

    // dest Pet is DogDto; source is AnimalDto — inference widens TValue to AnimalDto.
    public Mapper<Owner, OwnerDto> BuildMapWidened() =>
        CreateMapper<Owner, OwnerDto>()
            .Map(d => d.Pet, s => s.AsAnimal)
            .Build();

    public Mapper<Owner, OwnerDto> BuildOptionalWidened() =>
        CreateMapper<Owner, OwnerDto>()
            .Optional(d => d.Pet, s => s.AsAnimal)
            .Build();

    // Nested mapper targets the base DTO — the realistic trigger.
    public Mapper<Owner, OwnerDto> BuildNestedBaseMapper() =>
        CreateMapper<Owner, OwnerDto>()
            .Map(d => d.Pet, s => AnimalMapper.Map(s.Beast))
            .Build();

    public Mapper<Owner, OwnerDto> BuildNestedExactMapper() =>
        CreateMapper<Owner, OwnerDto>()
            .Map(d => d.Pet, s => DogMapper.Map(s.Pet))
            .Build();

    public Mapper<Owner, OwnerDto> BuildExact() =>
        CreateMapper<Owner, OwnerDto>()
            .Map(d => d.Pet, s => s.AsDog)
            .Build();

    // dest Pet is AnimalDto (base); map a DogDto source — assignable, must build.
    public Mapper<Owner, BaseHolderDto> BuildAssignableToBase() =>
        CreateMapper<Owner, BaseHolderDto>()
            .Map(d => d.Pet, s => s.AsDog)
            .Build();

    public Mapper<NumberSource, NumberDest> BuildNumericWidening() =>
        CreateMapper<NumberSource, NumberDest>()
            .Map(d => d.Value, s => s.Value)
            .Build();
}
