using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebShop.OData.Data;
using WebShop.OData.Models;

namespace WebShop.OData;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that replaces SQLite with
/// the EF Core in-memory provider and seeds a small deterministic dataset.
/// </summary>
public sealed class WebShopODataFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registration.
            ServiceDescriptor? descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<WebShopDbContext>));

            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database.
            services.AddDbContext<WebShopDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Seed deterministic test data.
            using ServiceProvider sp = services.BuildServiceProvider();
            using IServiceScope scope = sp.CreateScope();
            WebShopDbContext db = scope.ServiceProvider.GetRequiredService<WebShopDbContext>();
            db.Database.EnsureCreated();
            SeedTestData(db);
        });
    }

    private static void SeedTestData(WebShopDbContext db)
    {
        // Categories
        Category electronics = new() { Id = TestIds.ElectronicsCategory, Name = "Electronics", Description = "Electronic devices" };
        Category clothing    = new() { Id = TestIds.ClothingCategory, Name = "Clothing", Description = "Apparel and accessories" };

        db.Categories.AddRange(electronics, clothing);

        // Products
        Product laptop = new()
        {
            Id            = TestIds.Laptop,
            Name          = "Laptop Pro",
            Description   = "High-end laptop",
            Price         = 1299.99m,
            StockQuantity = 10,
            CategoryId    = electronics.Id,
            IsActive      = true,
            CreatedAt     = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        };
        Product phone = new()
        {
            Id            = TestIds.Phone,
            Name          = "SmartPhone X",
            Description   = "Latest smartphone",
            Price         = 899.00m,
            StockQuantity = 25,
            CategoryId    = electronics.Id,
            IsActive      = true,
            CreatedAt     = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        Product outOfStock = new()
        {
            Id            = TestIds.OutOfStockProduct,
            Name          = "Vintage Radio",
            Description   = "Classic radio",
            Price         = 49.99m,
            StockQuantity = 0,
            CategoryId    = electronics.Id,
            IsActive      = true,
            CreatedAt     = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        Product tShirt = new()
        {
            Id            = TestIds.TShirt,
            Name          = "Basic T-Shirt",
            Description   = "Cotton t-shirt",
            Price         = 19.99m,
            StockQuantity = 100,
            CategoryId    = clothing.Id,
            IsActive      = true,
            CreatedAt     = new DateTime(2025, 2, 10, 0, 0, 0, DateTimeKind.Utc),
        };
        Product inactiveProduct = new()
        {
            Id            = TestIds.InactiveProduct,
            Name          = "Discontinued Jacket",
            Description   = "No longer available",
            Price         = 79.99m,
            StockQuantity = 5,
            CategoryId    = clothing.Id,
            IsActive      = false,
            CreatedAt     = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        db.Products.AddRange(laptop, phone, outOfStock, tShirt, inactiveProduct);

        // Customers
        Customer alice = new()
        {
            Id              = TestIds.Alice,
            FirstName       = "Alice",
            LastName        = "Smith",
            Email           = "alice@example.com",
            PhoneNumber     = "555-0001",
            ShippingAddress = "123 Main St",
        };
        Customer bob = new()
        {
            Id              = TestIds.Bob,
            FirstName       = "Bob",
            LastName        = "Jones",
            Email           = "bob@example.com",
            PhoneNumber     = "555-0002",
            ShippingAddress = "456 Oak Ave",
        };

        db.Customers.AddRange(alice, bob);

        // Orders
        Order aliceOrder = new()
        {
            Id              = TestIds.AliceOrder,
            CustomerId      = alice.Id,
            PlacedAt        = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            Status          = OrderStatus.Delivered,
            ShippingAddress = "123 Main St",
        };
        Order bobOrder = new()
        {
            Id              = TestIds.BobOrder,
            CustomerId      = bob.Id,
            PlacedAt        = new DateTime(2025, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            Status          = OrderStatus.Pending,
            ShippingAddress = "456 Oak Ave",
        };

        db.Orders.AddRange(aliceOrder, bobOrder);

        // Order lines
        db.OrderLines.AddRange(
            new OrderLine
            {
                Id          = Guid.NewGuid(),
                OrderId     = aliceOrder.Id,
                ProductId   = laptop.Id,
                ProductName = "Laptop Pro",
                UnitPrice   = 1299.99m,
                Quantity    = 1,
            },
            new OrderLine
            {
                Id          = Guid.NewGuid(),
                OrderId     = aliceOrder.Id,
                ProductId   = tShirt.Id,
                ProductName = "Basic T-Shirt",
                UnitPrice   = 19.99m,
                Quantity    = 3,
            },
            new OrderLine
            {
                Id          = Guid.NewGuid(),
                OrderId     = bobOrder.Id,
                ProductId   = phone.Id,
                ProductName = "SmartPhone X",
                UnitPrice   = 899.00m,
                Quantity    = 2,
            });

        db.SaveChanges();
    }
}

/// <summary>Well-known IDs for deterministic test assertions.</summary>
public static class TestIds
{
    public static readonly Guid ElectronicsCategory = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid ClothingCategory    = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    public static readonly Guid Laptop          = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    public static readonly Guid Phone           = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    public static readonly Guid OutOfStockProduct = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");
    public static readonly Guid TShirt          = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004");
    public static readonly Guid InactiveProduct = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005");

    public static readonly Guid Alice      = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    public static readonly Guid Bob        = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    public static readonly Guid AliceOrder = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    public static readonly Guid BobOrder   = Guid.Parse("dddddddd-0000-0000-0000-000000000002");
}
