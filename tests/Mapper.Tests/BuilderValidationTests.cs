namespace ArchPillar.Mapper.Tests;

/// <summary>
/// Verifies that the builder catches configuration mistakes at build time,
/// making it impossible to silently produce an incomplete mapper.
/// </summary>
public class BuilderValidationTests
{
    // -----------------------------------------------------------------------
    // Unmapped destination property
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_WithUnmappedDestinationProperty_ThrowsInvalidOperationException()
    {
        // OrderLineDto has: ProductName, Quantity, UnitPrice, SupplierName
        // This mapper only covers ProductName and Quantity — UnitPrice and SupplierName are missing
        var context = new MinimalMapperContext();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => context.BuildIncomplete());

        // Exception must mention the unmapped property name so the developer can fix it
        Assert.Contains("UnitPrice", ex.Message);
    }

    [Fact]
    public void Build_WithAllPropertiesMapped_DoesNotThrow()
    {
        var context = new MinimalMapperContext();

        Exception? exception = Record.Exception(() => context.BuildComplete());
        Assert.Null(exception);
    }

    // -----------------------------------------------------------------------
    // Ignore counts as coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_WithIgnoredProperty_DoesNotThrow()
    {
        var context = new MinimalMapperContext();

        Exception? exception = Record.Exception(() => context.BuildWithIgnore());
        Assert.Null(exception);
    }

    // -----------------------------------------------------------------------
    // Optional counts as coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_WithOptionalProperty_DoesNotThrow()
    {
        var context = new MinimalMapperContext();

        Exception? exception = Record.Exception(() => context.BuildWithOptional());
        Assert.Null(exception);
    }

    // -----------------------------------------------------------------------
    // Member-init expression covers the properties it explicitly assigns
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_WithMemberInitCoveringAllProperties_DoesNotThrow()
    {
        var context = new MinimalMapperContext();

        Exception? exception = Record.Exception(() => context.BuildFromMemberInit());
        Assert.Null(exception);
    }

    [Fact]
    public void Build_WithMemberInitMissingProperty_ThrowsInvalidOperationException()
    {
        var context = new MinimalMapperContext();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            context.BuildFromPartialMemberInit());

        Assert.Contains("UnitPrice", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Eager build surfaces errors at construction time
    // -----------------------------------------------------------------------

    [Fact]
    public void EagerBuild_OnConstruction_SurfacesErrorImmediately()
    {
        // Constructing with EagerBuild = true must throw for an incomplete mapper
        Assert.Throws<InvalidOperationException>(() => new BrokenEagerMappers());
    }
}

// ---------------------------------------------------------------------------
// Helpers — minimal contexts used only in these tests
// ---------------------------------------------------------------------------

file class MinimalMapperContext : MapperContext
{
    public Mapper<OrderLine, OrderLineDto> BuildIncomplete() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            // UnitPrice and SupplierName are not covered — Build() must throw
            .Build();

    public Mapper<OrderLine, OrderLineDto> BuildComplete() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Map(d => d.UnitPrice, s => s.UnitPrice)
            .Map(d => d.SupplierName, s => s.SupplierName)
            .Build();

    public Mapper<OrderLine, OrderLineDto> BuildWithIgnore() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Map(d => d.UnitPrice, s => s.UnitPrice)
            .Ignore(d => d.SupplierName)
            .Build();

    public Mapper<OrderLine, OrderLineDto> BuildWithOptional() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Map(d => d.UnitPrice, s => s.UnitPrice)
            .Optional(d => d.SupplierName, s => s.SupplierName)
            .Build();

    public Mapper<OrderLine, OrderLineDto> BuildFromMemberInit() =>
        CreateMapper<OrderLine, OrderLineDto>(s => new OrderLineDto
        {
            ProductName = s.ProductName,
            Quantity = s.Quantity,
            UnitPrice = s.UnitPrice,
            SupplierName = s.SupplierName,
        })
        .Build();

    public Mapper<OrderLine, OrderLineDto> BuildFromPartialMemberInit() =>
        // Only covers ProductName and Quantity via fluent Map — UnitPrice is missing,
        // so Build() must throw.  (Using fluent style rather than member-init because
        // the C# compiler's `required` check would reject the incomplete initializer.)
        CreateMapper<OrderLine, OrderLineDto>()
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Build();
}

file class BrokenEagerMappers : MapperContext
{
    public Mapper<OrderLine, OrderLineDto> Line { get; }

    public BrokenEagerMappers()
    {
        // Incomplete mapping — UnitPrice not covered.
        // The implicit conversion to Mapper<,> triggers Build(), which throws.
        Line = CreateMapper<OrderLine, OrderLineDto>()
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity);
    }
}
