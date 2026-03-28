namespace ArchPillar.Extensions.Mapper.Tests;

// ---------------------------------------------------------------------------
// Test models
// ---------------------------------------------------------------------------

public class Ticket
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = [];
    public Customer? Assignee { get; set; }
}

// ---------------------------------------------------------------------------
// Test mapper contexts
// ---------------------------------------------------------------------------

public class CloneMappers : MapperContext
{
    public Mapper<Ticket, Ticket> Ticket { get; }

    public CloneMappers()
    {
        Ticket = CreateCloneMapper<Ticket>();
    }
}

public class CloneWithIgnoreMappers : MapperContext
{
    public Mapper<Ticket, Ticket> Ticket { get; }

    public CloneWithIgnoreMappers()
    {
        Ticket = CreateCloneMapper<Ticket>()
            .Ignore(dest => dest.Id);
    }
}

public class CloneWithMapOverrideMappers : MapperContext
{
    public Mapper<Ticket, Ticket> Ticket { get; }

    public CloneWithMapOverrideMappers()
    {
        Ticket = CreateCloneMapper<Ticket>()
            .Map(dest => dest.Tags, src => src.Tags.ToList());
    }
}

public class EagerCloneMappers : CloneMappers
{
    public EagerCloneMappers() { EagerBuildAll(); }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class CloneMapperTests
{
    private readonly CloneMappers _mappers = new();

    [Fact]
    public void Clone_CopiesAllScalarProperties()
    {
        var source = new Ticket
        {
            Id          = 42,
            Title       = "Bug report",
            Description = "Something is broken",
            Price       = 9.99m,
            CreatedAt   = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Notes       = "Urgent",
        };

        Ticket clone = _mappers.Ticket.Map(source)!;

        Assert.NotSame(source, clone);
        Assert.Equal(42, clone.Id);
        Assert.Equal("Bug report", clone.Title);
        Assert.Equal("Something is broken", clone.Description);
        Assert.Equal(9.99m, clone.Price);
        Assert.Equal(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), clone.CreatedAt);
        Assert.Equal("Urgent", clone.Notes);
    }

    [Fact]
    public void Clone_NullSource_ReturnsNull()
    {
        Ticket? clone = _mappers.Ticket.Map((Ticket?)null);

        Assert.Null(clone);
    }

    [Fact]
    public void Clone_ReferenceProperty_IsShallowCopy()
    {
        var assignee = new Customer { Name = "Alice", Email = "alice@test.com" };
        var source = new Ticket
        {
            Id          = 1,
            Title       = "Task",
            Description = "Do something",
            Assignee    = assignee,
        };

        Ticket clone = _mappers.Ticket.Map(source)!;

        Assert.Same(source.Assignee, clone.Assignee);
    }

    [Fact]
    public void Clone_Collection_IsShallowCopy()
    {
        var source = new Ticket
        {
            Id          = 1,
            Title       = "Task",
            Description = "Do something",
            Tags        = ["alpha", "beta"],
        };

        Ticket clone = _mappers.Ticket.Map(source)!;

        Assert.Same(source.Tags, clone.Tags);
    }

    [Fact]
    public void Clone_NullableProperty_CopiesNull()
    {
        var source = new Ticket
        {
            Id          = 1,
            Title       = "Task",
            Description = "Do something",
            Notes       = null,
            Assignee    = null,
        };

        Ticket clone = _mappers.Ticket.Map(source)!;

        Assert.Null(clone.Notes);
        Assert.Null(clone.Assignee);
    }

    [Fact]
    public void Clone_WithIgnore_SkipsIgnoredProperty()
    {
        var mappers = new CloneWithIgnoreMappers();
        var source = new Ticket
        {
            Id          = 99,
            Title       = "Task",
            Description = "Do something",
        };

        Ticket clone = mappers.Ticket.Map(source)!;

        Assert.Equal(0, clone.Id);
        Assert.Equal("Task", clone.Title);
        Assert.Equal("Do something", clone.Description);
    }

    [Fact]
    public void Clone_WithMapOverride_UsesOverriddenExpression()
    {
        var mappers = new CloneWithMapOverrideMappers();
        var source = new Ticket
        {
            Id          = 1,
            Title       = "Task",
            Description = "Do something",
            Tags        = ["alpha", "beta"],
        };

        Ticket clone = mappers.Ticket.Map(source)!;

        Assert.NotSame(source.Tags, clone.Tags);
        Assert.Equal(source.Tags, clone.Tags);
    }

    [Fact]
    public void Clone_MapTo_UpdatesExistingObject()
    {
        var source = new Ticket
        {
            Id          = 42,
            Title       = "Original",
            Description = "Source description",
            Price       = 19.99m,
        };
        var target = new Ticket
        {
            Id          = 0,
            Title       = "Placeholder",
            Description = "Old",
        };

        _mappers.Ticket.MapTo(source, target);

        Assert.Equal(42, target.Id);
        Assert.Equal("Original", target.Title);
        Assert.Equal("Source description", target.Description);
        Assert.Equal(19.99m, target.Price);
    }

    [Fact]
    public void Clone_ToExpression_ProducesValidExpression()
    {
        var expression = _mappers.Ticket.ToExpression();

        Assert.NotNull(expression);
        Assert.IsAssignableFrom<System.Linq.Expressions.LambdaExpression>(expression);
    }

    [Fact]
    public void Clone_EagerBuild_DoesNotThrow()
    {
        var mappers = new EagerCloneMappers();

        Assert.NotNull(mappers.Ticket);
    }
}
