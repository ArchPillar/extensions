using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

// DTOs for the Invoke scenario.
public sealed class InvokeCustomerDto
{
    public string Name { get; set; } = "";
}

public sealed class InvokeOrderDto
{
    public int Id { get; set; }
    public InvokeCustomerDto? Customer { get; set; }
}

public sealed class InvokeWrapperDto
{
    public InvokeOrderDto? Inner { get; set; }
}

/// <summary>
/// Mirrors the bug report: an inner mapper whose body routes through an
/// <em>instance</em> method (capturing the context), used inside an outer
/// mapper. Inlining the inner mapper embeds the instance call in the
/// projection, which EF Core rejects ("constant expression ... through the
/// instance method"). <c>Invoke</c> opts the inner mapper out of inlining so
/// the provider invokes it on the materialised source instead.
/// </summary>
public sealed class InvokeMappers : MapperContext
{
    public Mapper<Customer, InvokeCustomerDto> Customer { get; }

    /// <summary>Outer mapper that opts out of inlining via <c>Invoke</c>.</summary>
    public Mapper<Order, InvokeOrderDto> OrderInvoke { get; }

    /// <summary>Outer mapper that inlines the inner mapper (reproduces the bug).</summary>
    public Mapper<Order, InvokeOrderDto> OrderInlined { get; }

    /// <summary>Grandparent that inlines <see cref="OrderInvoke"/> (which itself uses <c>Invoke</c>).</summary>
    public Mapper<Order, InvokeWrapperDto> Wrapper { get; }

    // Instance method — captures `this`, untranslatable by EF Core.
    private InvokeCustomerDto BuildCustomer(Customer c)
        => new() { Name = new string(c.Name.Reverse().ToArray()) };

    public InvokeMappers()
    {
        Customer = CreateMapper<Customer, InvokeCustomerDto>(src => new InvokeCustomerDto
        {
            Name = BuildCustomer(src).Name,
        });

        OrderInvoke = CreateMapper<Order, InvokeOrderDto>(src => new InvokeOrderDto
        {
            Id       = src.Id,
            Customer = Customer.Invoke(src.Customer),
        });

        OrderInlined = CreateMapper<Order, InvokeOrderDto>(src => new InvokeOrderDto
        {
            Id       = src.Id,
            Customer = Customer.Map(src.Customer),
        });

        Wrapper = CreateMapper<Order, InvokeWrapperDto>(src => new InvokeWrapperDto
        {
            Inner = OrderInvoke.Map(src),
        });
    }
}

public sealed class InvokeInMemoryTests
{
    private readonly InvokeMappers _mappers = new();

    [Fact]
    public void Invoke_InMemoryObjectMapping_MapsNestedObject()
    {
        var order = new Order
        {
            Id        = 1,
            CreatedAt = DateTime.UtcNow,
            Status    = OrderStatus.Pending,
            OwnerId   = 10,
            Customer  = new Customer { Name = "Alice", Email = "alice@example.com" },
            Lines     = [],
        };

        InvokeOrderDto? dto = _mappers.OrderInvoke.Map(order);

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
        Assert.NotNull(dto.Customer);
        Assert.Equal("ecilA", dto.Customer.Name);
    }

    [Fact]
    public void Invoke_ToExpression_IsNotInlined()
    {
        // Intent lock: unlike Map(), Invoke must NOT be spliced into the
        // projection. The call survives so the provider invokes it at runtime.
        var expr = _mappers.OrderInvoke.ToExpression();

        Assert.True(
            ContainsInvokeCall(expr),
            "Invoke call should be preserved in the expression, not inlined.");
    }

    private static bool ContainsInvokeCall(Expression expression)
    {
        var finder = new InvokeCallFinder();
        finder.Visit(expression);
        return finder.Found;
    }

    private sealed class InvokeCallFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "Invoke"
                && node.Method.DeclaringType is { IsGenericType: true } declaring
                && declaring.GetGenericTypeDefinition() == typeof(Mapper<,>))
            {
                Found = true;
            }

            return base.VisitMethodCall(node);
        }
    }

    [Fact]
    public void Invoke_NullSource_ReturnsNull()
    {
        var order = new Order
        {
            Id        = 2,
            CreatedAt = DateTime.UtcNow,
            Status    = OrderStatus.Pending,
            OwnerId   = 20,
            Customer  = null!,
            Lines     = [],
        };

        InvokeOrderDto? dto = _mappers.OrderInvoke.Map(order);

        Assert.NotNull(dto);
        Assert.Null(dto.Customer);
    }
}

[Collection("PostgreSQL")]
public sealed class InvokePostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private PostgresTestDbContext _db = null!;
    private readonly InvokeMappers _mappers = new();

    public async Task InitializeAsync()
    {
        _postgres = await PostgresTestDatabase.CreateAsync(fixture);
        DbContextOptions<PostgresTestDbContext> options = new DbContextOptionsBuilder<PostgresTestDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;
        _db = new PostgresTestDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _db.Orders.Add(new Order
        {
            Id        = 1,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status    = OrderStatus.Pending,
            OwnerId   = 10,
            Customer  = new Customer { Name = "Alice", Email = "alice@example.com" },
            Lines     = [],
        });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Invoke_Projection_TranslatesAndEvaluatesAtRuntimeAsync()
    {
        InvokeOrderDto result = await _db.Orders
            .Where(o => o.Id == 1)
            .Project(_mappers.OrderInvoke)
            .SingleAsync();

        Assert.Equal(1, result.Id);
        Assert.NotNull(result.Customer);
        Assert.Equal("ecilA", result.Customer.Name);
    }

    [Fact]
    public async Task Invoke_NestedInsideInlinedGrandparent_TranslatesAsync()
    {
        // The grandparent inlines OrderInvoke, which itself uses Invoke. The
        // Invoke call must still be left for runtime evaluation at this depth.
        InvokeWrapperDto result = await _db.Orders
            .Where(o => o.Id == 1)
            .Project(_mappers.Wrapper)
            .SingleAsync();

        Assert.NotNull(result.Inner);
        Assert.NotNull(result.Inner.Customer);
        Assert.Equal("ecilA", result.Inner.Customer.Name);
    }

    [Fact]
    public async Task InlinedInstanceMethod_Projection_ThrowsCapturedConstantAsync()
    {
        // Documents the bug Invoke exists to avoid: inlining the inner mapper
        // embeds the instance method call in the SQL projection, which EF rejects.
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Orders
                .Where(o => o.Id == 1)
                .Project(_mappers.OrderInlined)
                .SingleAsync());

        Assert.Contains("instance method", ex.Message, StringComparison.Ordinal);
    }
}
