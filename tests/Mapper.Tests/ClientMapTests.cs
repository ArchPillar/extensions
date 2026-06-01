using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

// DTOs for the ClientMap scenario.
public sealed class CustomerClientDto
{
    public string Name { get; set; } = "";
}

public sealed class OrderClientDto
{
    public int Id { get; set; }
    public CustomerClientDto? Customer { get; set; }
}

/// <summary>
/// Mirrors the bug report: an inner mapper whose body routes through an
/// <em>instance</em> method (capturing the context), used inside an outer
/// mapper. Inlining the inner mapper embeds the instance call in the
/// projection, which EF Core rejects ("constant expression ... through the
/// instance method"). <c>ClientMap</c> opts the inner mapper out of inlining
/// so EF materializes the source and runs it client-side instead.
/// </summary>
public sealed class ClientMapMappers : MapperContext
{
    public Mapper<Customer, CustomerClientDto> Customer { get; }

    /// <summary>Outer mapper that opts out of inlining via <c>ClientMap</c>.</summary>
    public Mapper<Order, OrderClientDto> OrderClient { get; }

    /// <summary>Outer mapper that inlines the inner mapper (reproduces the bug).</summary>
    public Mapper<Order, OrderClientDto> OrderInlined { get; }

    // Instance method — captures `this`, untranslatable by EF Core.
    private CustomerClientDto BuildCustomer(Customer c)
        => new() { Name = new string(c.Name.Reverse().ToArray()) };

    public ClientMapMappers()
    {
        Customer = CreateMapper<Customer, CustomerClientDto>(src => new CustomerClientDto
        {
            Name = BuildCustomer(src).Name,
        });

        OrderClient = CreateMapper<Order, OrderClientDto>(src => new OrderClientDto
        {
            Id       = src.Id,
            Customer = Customer.ClientMap(src.Customer),
        });

        OrderInlined = CreateMapper<Order, OrderClientDto>(src => new OrderClientDto
        {
            Id       = src.Id,
            Customer = Customer.Map(src.Customer),
        });

        Wrapper = CreateMapper<Order, OrderWrapperDto>(src => new OrderWrapperDto
        {
            Inner = OrderClient.Map(src),
        });
    }

    /// <summary>Grandparent that inlines <see cref="OrderClient"/> (which itself uses <c>ClientMap</c>).</summary>
    public Mapper<Order, OrderWrapperDto> Wrapper { get; }
}

public sealed class OrderWrapperDto
{
    public OrderClientDto? Inner { get; set; }
}

public sealed class ClientMapInMemoryTests
{
    private readonly ClientMapMappers _mappers = new();

    [Fact]
    public void ClientMap_InMemoryObjectMapping_MapsNestedObject()
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

        OrderClientDto? dto = _mappers.OrderClient.Map(order);

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
        Assert.NotNull(dto.Customer);
        Assert.Equal("ecilA", dto.Customer.Name);
    }

    [Fact]
    public void ClientMap_ToExpression_IsNotInlined()
    {
        // Intent lock: unlike Map(), ClientMap must NOT be spliced into the
        // projection. The call survives so the provider evaluates it client-side.
        var expr = _mappers.OrderClient.ToExpression();

        Assert.True(
            ContainsClientMapCall(expr),
            "ClientMap call should be preserved in the expression, not inlined.");
    }

    private static bool ContainsClientMapCall(Expression expression)
    {
        var finder = new ClientMapCallFinder();
        finder.Visit(expression);
        return finder.Found;
    }

    private sealed class ClientMapCallFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "ClientMap")
            {
                Found = true;
            }

            return base.VisitMethodCall(node);
        }
    }

    [Fact]
    public void ClientMap_NullSource_ReturnsNull()
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

        OrderClientDto? dto = _mappers.OrderClient.Map(order);

        Assert.NotNull(dto);
        Assert.Null(dto.Customer);
    }
}

[Collection("PostgreSQL")]
public sealed class ClientMapPostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _postgres = null!;
    private PostgresTestDbContext _db = null!;
    private readonly ClientMapMappers _mappers = new();

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
    public async Task ClientMap_Projection_TranslatesAndEvaluatesClientSideAsync()
    {
        OrderClientDto result = await _db.Orders
            .Where(o => o.Id == 1)
            .Project(_mappers.OrderClient)
            .SingleAsync();

        Assert.Equal(1, result.Id);
        Assert.NotNull(result.Customer);
        Assert.Equal("ecilA", result.Customer.Name);
    }

    [Fact]
    public async Task ClientMap_NestedInsideInlinedGrandparent_TranslatesAsync()
    {
        // The grandparent inlines OrderClient, which itself uses ClientMap. The
        // ClientMap call must still be left for client evaluation at this depth.
        OrderWrapperDto result = await _db.Orders
            .Where(o => o.Id == 1)
            .Project(_mappers.Wrapper)
            .SingleAsync();

        Assert.NotNull(result.Inner);
        Assert.NotNull(result.Inner.Customer);
        Assert.Equal("ecilA", result.Inner.Customer.Name);
    }

    [Fact]
    public async Task InlinedInstanceMethod_Projection_ThrowsClientConstantAsync()
    {
        // Documents the bug ClientMap exists to avoid: inlining the inner mapper
        // embeds the instance method call in the SQL projection, which EF rejects.
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Orders
                .Where(o => o.Id == 1)
                .Project(_mappers.OrderInlined)
                .SingleAsync());

        Assert.Contains("instance method", ex.Message, StringComparison.Ordinal);
    }
}
