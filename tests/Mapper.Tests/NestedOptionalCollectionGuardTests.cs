namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies that the in-memory null guard for optional collection sources
/// survives when a mapper is inlined into a parent mapper. The guard is only
/// applied by the top-level <c>BuildMapExpression</c>; without propagation it
/// is silently dropped at the nesting boundary, so a non-null parent with a
/// null nested optional collection throws <see cref="ArgumentNullException"/>
/// out of <c>Enumerable.Select</c> during in-memory mapping.
/// </summary>
public class NestedOptionalCollectionGuardTests
{
    [Fact]
    public void Map_TopLevelOptionalCollectionNull_MapsToNull()
    {
        var mappers = new NestedGuardMappers();
        var source = new FormSource { Id = 1, Options = null };

        FormDest? dest = mappers.Form.Map(source);

        Assert.NotNull(dest);
        Assert.Equal(1, dest.Id);
        Assert.Null(dest.Options);
    }

    [Fact]
    public void Map_NestedOptionalCollectionNull_MapsToNull()
    {
        var mappers = new NestedGuardMappers();
        var source = new PageSource
        {
            Id = 1,
            Form = new FormSource { Id = 2, Options = null },
        };

        PageDest? dest = mappers.Page.Map(source);

        Assert.NotNull(dest);
        Assert.NotNull(dest.Form);
        Assert.Equal(2, dest.Form!.Id);
        Assert.Null(dest.Form.Options);
    }

    [Fact]
    public void Map_NestedOptionalCollectionNonNull_MapsElements()
    {
        var mappers = new NestedGuardMappers();
        var source = new PageSource
        {
            Id = 1,
            Form = new FormSource
            {
                Id = 2,
                Options = [new OptionSource { Label = "A" }, new OptionSource { Label = "B" }],
            },
        };

        PageDest? dest = mappers.Page.Map(source);

        Assert.NotNull(dest!.Form!.Options);
        Assert.Equal(2, dest.Form.Options!.Count);
        Assert.Equal("A", dest.Form.Options[0].Label);
    }

    [Fact]
    public void Map_NestedParentNull_MapsFormToNull()
    {
        var mappers = new NestedGuardMappers();
        var source = new PageSource { Id = 1, Form = null };

        PageDest? dest = mappers.Page.Map(source);

        Assert.NotNull(dest);
        Assert.Null(dest!.Form);
    }
}

// ---------------------------------------------------------------------------
// Test-local models
// ---------------------------------------------------------------------------

file class OptionSource
{
    public required string Label { get; set; }
}

file class OptionDest
{
    public required string Label { get; set; }
}

file class FormSource
{
    public required int Id { get; set; }
    public List<OptionSource>? Options { get; set; }
}

file class FormDest
{
    public required int Id { get; set; }
    public List<OptionDest>? Options { get; set; }
}

file class PageSource
{
    public required int Id { get; set; }
    public FormSource? Form { get; set; }
}

file class PageDest
{
    public required int Id { get; set; }
    public FormDest? Form { get; set; }
}

// ---------------------------------------------------------------------------
// Test-local mappers
// ---------------------------------------------------------------------------

file class NestedGuardMappers : MapperContext
{
    public Mapper<OptionSource, OptionDest> Option { get; }
    public Mapper<FormSource, FormDest> Form { get; }
    public Mapper<PageSource, PageDest> Page { get; }

    public NestedGuardMappers()
    {
        Option = CreateMapper<OptionSource, OptionDest>(s => new OptionDest
        {
            Label = s.Label,
        });

        Form = CreateMapper<FormSource, FormDest>(s => new FormDest
        {
            Id = s.Id,
        })
        .Optional(d => d.Options, s => s.Options!.Project(Option).ToList());

        Page = CreateMapper<PageSource, PageDest>(s => new PageDest
        {
            Id   = s.Id,
            Form = Form.Map(s.Form),
        });
    }
}
