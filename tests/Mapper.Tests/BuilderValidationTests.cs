namespace ArchPillar.Extensions.Mapper.Tests;

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

    // -----------------------------------------------------------------------
    // Eager build includes enum mappers
    // -----------------------------------------------------------------------

    [Fact]
    public void EagerBuild_WithBrokenEnumMapper_SurfacesErrorImmediately()
    {
        // An enum mapper whose mapping method throws for a valid value must
        // fail at EagerBuildAll time, not at first use.
        Assert.ThrowsAny<Exception>(() => new BrokenEnumEagerMappers());
    }

    // -----------------------------------------------------------------------
    // Coverage validation modes
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // CoverageValidation.AllProperties
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_AllProperties_AllCovered_DoesNotThrow()
    {
        var context = new CoverageValidationContext();

        Exception? exception = Record.Exception(
            () => context.BuildAllPropertiesFullCoverage());
        Assert.Null(exception);
    }

    [Fact]
    public void Build_AllProperties_UnmappedNullableProperty_ThrowsInvalidOperationException()
    {
        var context = new CoverageValidationContext();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => context.BuildAllPropertiesMissingNullable());

        Assert.Contains("SupplierName", ex.Message);
    }

    // -----------------------------------------------------------------------
    // CoverageValidation.NonNullableProperties
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_NonNullableProperties_UnmappedNullableProperty_DoesNotThrow()
    {
        var context = new CoverageValidationContext();

        Exception? exception = Record.Exception(
            () => context.BuildNonNullableSkipsNullable());
        Assert.Null(exception);
    }

    [Fact]
    public void Build_NonNullableProperties_UnmappedNonNullableProperty_ThrowsInvalidOperationException()
    {
        var context = new CoverageValidationContext();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => context.BuildNonNullableMissingRequired());

        Assert.Contains("UnitPrice", ex.Message);
    }

    // -----------------------------------------------------------------------
    // CoverageValidation.None
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_None_UnmappedRequiredProperty_DoesNotThrow()
    {
        var context = new CoverageValidationContext();

        Exception? exception = Record.Exception(
            () => context.BuildNoneSkipsAll());
        Assert.Null(exception);
    }

    // -----------------------------------------------------------------------
    // Context default + per-builder override
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_ContextDefaultAllProperties_AppliedWithoutOverride()
    {
        var context = new AllPropertiesDefaultContext();

        Assert.Throws<InvalidOperationException>(
            () => context.BuildWithoutOverride());
    }

    [Fact]
    public void Build_ContextDefaultAllProperties_OverriddenByBuilder()
    {
        var context = new AllPropertiesDefaultContext();

        Exception? exception = Record.Exception(
            () => context.BuildWithBuilderOverride());
        Assert.Null(exception);
    }

    // -----------------------------------------------------------------------
    // Constructor validation — parameterized constructor expression rejected
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_WithParameterizedConstructorExpression_ThrowsInvalidOperationException()
    {
        var context = new ConstructorExpressionValidationContext();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => context.BuildWithConstructorArgs());

        Assert.Contains("parameterized constructor", ex.Message);
        Assert.Contains("DualConstructorDto", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Circular mapper reference detection
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_WithCircularMapperReference_ThrowsInvalidOperationException()
    {
        var context = new CircularMapperContext();
        var source = new TreeNode { Name = "root" };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => context.TreeNode.Map(source));

        Assert.Contains("nesting depth", ex.Message);
    }

    [Fact]
    public void ToExpression_WithCircularMapperReference_ThrowsInvalidOperationException()
    {
        var context = new CircularMapperContext();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => context.TreeNode.ToExpression());

        Assert.Contains("nesting depth", ex.Message);
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

file class BrokenEnumEagerMappers : MapperContext
{
    public EnumMapper<OrderStatus, OrderStatusDto> StatusMapper { get; }

    public BrokenEnumEagerMappers()
    {
        // Mapping method throws for Cancelled — EagerBuildAll must surface this.
        StatusMapper = CreateEnumMapper<OrderStatus, OrderStatusDto>(s => s switch
        {
            OrderStatus.Pending => OrderStatusDto.Pending,
            OrderStatus.Shipped => OrderStatusDto.Shipped,
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        });

        EagerBuildAll();
    }
}

file class CoverageValidationContext : MapperContext
{
    // AllProperties — positive: every property (including nullable) is covered
    public Mapper<OrderLine, OrderLineDto> BuildAllPropertiesFullCoverage() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .SetCoverageValidation(CoverageValidation.AllProperties)
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Map(d => d.UnitPrice, s => s.UnitPrice)
            .Map(d => d.SupplierName, s => s.SupplierName)
            .Ignore(d => d.Product)
            .Build();

    // AllProperties — negative: nullable SupplierName and Product not covered
    public Mapper<OrderLine, OrderLineDto> BuildAllPropertiesMissingNullable() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .SetCoverageValidation(CoverageValidation.AllProperties)
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Map(d => d.UnitPrice, s => s.UnitPrice)
            .Build();

    // NonNullableProperties — positive: nullable properties auto-ignored
    public Mapper<OrderLine, OrderLineDto> BuildNonNullableSkipsNullable() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .SetCoverageValidation(CoverageValidation.NonNullableProperties)
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Map(d => d.UnitPrice, s => s.UnitPrice)
            .Build();

    // NonNullableProperties — negative: non-nullable UnitPrice not covered
    public Mapper<OrderLine, OrderLineDto> BuildNonNullableMissingRequired() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .SetCoverageValidation(CoverageValidation.NonNullableProperties)
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Build();

    // None — positive: nothing validated, missing non-nullable UnitPrice is fine
    public Mapper<OrderLine, OrderLineDto> BuildNoneSkipsAll() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .SetCoverageValidation(CoverageValidation.None)
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Build();
}

file class AllPropertiesDefaultContext : MapperContext
{
    protected override CoverageValidation DefaultCoverageValidation
        => CoverageValidation.AllProperties;

    public Mapper<OrderLine, OrderLineDto> BuildWithBuilderOverride() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .SetCoverageValidation(CoverageValidation.None)
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            // Missing UnitPrice — but builder overrides to None
            .Build();

    public Mapper<OrderLine, OrderLineDto> BuildWithoutOverride() =>
        CreateMapper<OrderLine, OrderLineDto>()
            .Map(d => d.ProductName, s => s.ProductName)
            .Map(d => d.Quantity, s => s.Quantity)
            .Map(d => d.UnitPrice, s => s.UnitPrice)
            // SupplierName is nullable — but context default is AllProperties, so it must be covered
            .Build();
}

file class ConstructorExpressionValidationContext : MapperContext
{
    public Mapper<OrderLine, DualConstructorDto> BuildWithConstructorArgs() =>
        CreateMapper<OrderLine, DualConstructorDto>(s => new DualConstructorDto(s.ProductName, s.Quantity))
            .Build();
}

file class CircularMapperContext : MapperContext
{
    public Mapper<TreeNode, TreeNodeDto> TreeNode { get; private set; } = null!;

    public CircularMapperContext()
    {
        TreeNode = CreateMapper<TreeNode, TreeNodeDto>()
            .Map(d => d.Name, s => s.Name)
            .Map(d => d.Child, s => TreeNode.Map(s.Child))
            .Build();
    }
}
