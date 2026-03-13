namespace ArchPillar.Mapper.Tests;

/// <summary>
/// Verifies the capabilities unlocked by replacing <c>NestedMapperDetector</c>
/// with <c>NestedMapperInliner</c>:
/// <list type="bullet">
/// <item>Multiple nested mapper calls within a single property expression
/// (e.g. a ternary selecting between two mappers).</item>
/// <item>Nested mapper calls inside <c>ToDictionary</c> value selectors.</item>
/// </list>
/// </summary>
public class NestedInlinerTests
{
    private readonly InlinerTestMappers _mappers = new();

    // -----------------------------------------------------------------------
    // Issue 2 — multiple Map() calls in one property expression
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_TernaryBetweenTwoMapperCalls_SelectsFirstBranch()
    {
        var source = new FlexSource
        {
            UseFirst = true,
            First    = new PartSource { Text = "alpha" },
            Second   = new PartSource { Text = "beta"  },
        };

        FlexDest? dest = _mappers.Flex.Map(source);

        Assert.Equal("alpha", dest!.Part.Label);
    }

    [Fact]
    public void Map_TernaryBetweenTwoMapperCalls_SelectsSecondBranch()
    {
        var source = new FlexSource
        {
            UseFirst = false,
            First    = new PartSource { Text = "alpha" },
            Second   = new PartSource { Text = "beta"  },
        };

        FlexDest? dest = _mappers.Flex.Map(source);

        Assert.Equal("beta", dest!.Part.Label);
    }

    [Fact]
    public void Project_TernaryBetweenTwoMapperCalls_SelectsCorrectBranch()
    {
        IQueryable<FlexSource> sources = new[]
        {
            new FlexSource { UseFirst = true,  First = new PartSource { Text = "alpha" }, Second = new PartSource { Text = "beta" } },
            new FlexSource { UseFirst = false, First = new PartSource { Text = "alpha" }, Second = new PartSource { Text = "beta" } },
        }.AsQueryable();

        var results = sources.Project(_mappers.Flex).ToList();

        Assert.Equal("alpha", results[0].Part.Label);
        Assert.Equal("beta",  results[1].Part.Label);
    }

    // -----------------------------------------------------------------------
    // Issue 3 — Map() inside a ToDictionary value selector
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NestedMapperInsideToDictionary_ProducesCorrectDictionary()
    {
        var source = new Catalog
        {
            Items =
            [
                new CatalogItem { Key = "A", Display = "Apple"  },
                new CatalogItem { Key = "B", Display = "Banana" },
            ],
        };

        CatalogDto? dto = _mappers.Catalog.Map(source);

        Assert.Equal(2, dto!.Items.Count);
        Assert.Equal("Apple",  dto.Items["A"].Label);
        Assert.Equal("Banana", dto.Items["B"].Label);
    }

    [Fact]
    public void Project_NestedMapperInsideToDictionary_ProducesCorrectDictionary()
    {
        IQueryable<Catalog> sources = new[]
        {
            new Catalog
            {
                Items =
                [
                    new CatalogItem { Key = "X", Display = "Xenon" },
                    new CatalogItem { Key = "Y", Display = "Yttrium" },
                ],
            },
        }.AsQueryable();

        var results = sources.Project(_mappers.Catalog).ToList();

        Assert.Equal("Xenon",   results[0].Items["X"].Label);
        Assert.Equal("Yttrium", results[0].Items["Y"].Label);
    }

    // -----------------------------------------------------------------------
    // Map() calls inside an inline new {} object initializer
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NestedMapperInsideInlineObjectInitializer_ProducesCorrectResult()
    {
        var source = new ShipmentSource
        {
            Id   = "S1",
            Pack = new PackSource
            {
                Primary   = new PartSource { Text = "alpha", Tag = "t1" },
                Secondary = new PartSource { Text = "beta",  Tag = "t2" },
            },
        };

        ShipmentDest? dest = _mappers.Shipment.Map(source);

        Assert.Equal("alpha", dest!.Pack.Primary.Label);
        Assert.Equal("beta",  dest.Pack.Secondary.Label);
    }

    [Fact]
    public void Project_NestedMapperInsideInlineObjectInitializer_ProducesCorrectResult()
    {
        IQueryable<ShipmentSource> sources = new[]
        {
            new ShipmentSource
            {
                Id   = "S1",
                Pack = new PackSource
                {
                    Primary   = new PartSource { Text = "alpha", Tag = "t1" },
                    Secondary = new PartSource { Text = "beta",  Tag = "t2" },
                },
            },
        }.AsQueryable();

        var results = sources.Project(_mappers.Shipment).ToList();

        Assert.Equal("alpha", results[0].Pack.Primary.Label);
        Assert.Equal("beta",  results[0].Pack.Secondary.Label);
    }

    // -----------------------------------------------------------------------
    // Includes cascade through the inline new {} into the nested mapper,
    // following the property path.  "Pack.Primary.Tag" reaches the Tag
    // optional only inside the Primary binding, leaving Secondary unaffected.
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_InMemory_OptionalInNestedMapperInsideInlineInitializer_AlwaysPopulated()
    {
        var source = new ShipmentSource
        {
            Id   = "S1",
            Pack = new PackSource
            {
                Primary   = new PartSource { Text = "alpha", Tag = "t1" },
                Secondary = new PartSource { Text = "beta",  Tag = "t2" },
            },
        };

        ShipmentDest? dest = _mappers.Shipment.Map(source);

        // Map() always includes all optionals
        Assert.Equal("t1", dest!.Pack.Primary.Tag);
        Assert.Equal("t2", dest.Pack.Secondary.Tag);
    }

    [Fact]
    public void Project_WithoutIncludes_OptionalInNestedMapperInsideInlineInitializer_IsNull()
    {
        IQueryable<ShipmentSource> sources = new[]
        {
            new ShipmentSource
            {
                Id   = "S1",
                Pack = new PackSource
                {
                    Primary   = new PartSource { Text = "alpha", Tag = "t1" },
                    Secondary = new PartSource { Text = "beta",  Tag = "t2" },
                },
            },
        }.AsQueryable();

        var results = sources.Project(_mappers.Shipment).ToList();

        Assert.Null(results[0].Pack.Primary.Tag);
        Assert.Null(results[0].Pack.Secondary.Tag);
    }

    [Fact]
    public void Project_WithIncludes_OptionalInNestedMapperInsideInlineInitializer_IsPopulated()
    {
        IQueryable<ShipmentSource> sources = new[]
        {
            new ShipmentSource
            {
                Id   = "S1",
                Pack = new PackSource
                {
                    Primary   = new PartSource { Text = "alpha", Tag = "t1" },
                    Secondary = new PartSource { Text = "beta",  Tag = "t2" },
                },
            },
        }.AsQueryable();

        // "Pack.Primary.Tag" follows the property path through the inline Pack
        // initializer into only the Primary binding — Secondary.Tag stays null
        var results = sources.Project(_mappers.Shipment, o => o.Include("Pack.Primary.Tag")).ToList();

        Assert.Equal("t1", results[0].Pack.Primary.Tag);
        Assert.Null(results[0].Pack.Secondary.Tag);
    }

    // -----------------------------------------------------------------------
    // Include validation through inline object initializers
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_UnknownPropertyInInlineInitializer_ThrowsInvalidOperationException()
    {
        IQueryable<ShipmentSource> sources = new[]
        {
            new ShipmentSource
            {
                Id   = "S1",
                Pack = new PackSource
                {
                    Primary   = new PartSource { Text = "alpha", Tag = "t1" },
                    Secondary = new PartSource { Text = "beta",  Tag = "t2" },
                },
            },
        }.AsQueryable();

        Assert.Throws<InvalidOperationException>(() =>
            sources.Project(_mappers.Shipment, o => o.Include("Pack.Typo.Tag")).ToList());
    }
}
