using Bogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebShop.OData.Models;

namespace WebShop.OData.Data;

/// <summary>
/// Populates the database with realistic fake data using Bogus.
/// All operations are idempotent — existing data is never overwritten.
/// </summary>
public static class Seeder
{
    /// <summary>
    /// Seeds the database. Safe to call on an already-seeded database —
    /// each entity group is skipped when at least one row already exists.
    /// </summary>
    public static async Task SeedAsync(
        WebShopDbContext db,
        ILogger logger,
        int categoryCount = 8,
        int productCount  = 60,
        int customerCount = 25)
    {
        logger.LogInformation("Seeding database…");
        await SeedCategoriesAsync(db, categoryCount, logger);
        await SeedProductsAsync(db, productCount, logger);
        await SeedCustomersAsync(db, customerCount, logger);
        logger.LogInformation("Seeding complete.");
    }

    private static async Task SeedCategoriesAsync(WebShopDbContext db, int count, ILogger logger)
    {
        if (await db.Categories.AnyAsync())
        {
            return;
        }

        Faker<Category> faker = new Faker<Category>()
            .RuleFor(c => c.Id,          _ => Guid.NewGuid())
            .RuleFor(c => c.Name,        f => f.Commerce.Department())
            .RuleFor(c => c.Description, f => f.Lorem.Sentence());

        db.Categories.AddRange(faker.Generate(count));
        await db.SaveChangesAsync();
        logger.LogInformation("{Count} categories created.", count);
    }

    private static async Task SeedProductsAsync(WebShopDbContext db, int count, ILogger logger)
    {
        if (await db.Products.AnyAsync())
        {
            return;
        }

        List<Guid> categoryIds = await db.Categories.Select(c => c.Id).ToListAsync();

        Faker<Product> faker = new Faker<Product>()
            .RuleFor(p => p.Id,            _ => Guid.NewGuid())
            .RuleFor(p => p.Name,          f => f.Commerce.ProductName())
            .RuleFor(p => p.Description,   f => f.Commerce.ProductDescription())
            .RuleFor(p => p.Price,         f => decimal.Round(f.Random.Decimal(1m, 500m), 2))
            .RuleFor(p => p.StockQuantity, f => f.Random.Int(0, 200))
            .RuleFor(p => p.CategoryId,    f => f.PickRandom(categoryIds))
            .RuleFor(p => p.IsActive,      f => f.Random.Bool(0.9f))
            .RuleFor(p => p.CreatedAt,     f => f.Date.Past(2).ToUniversalTime());

        db.Products.AddRange(faker.Generate(count));
        await db.SaveChangesAsync();
        logger.LogInformation("{Count} products created.", count);
    }

    private static async Task SeedCustomersAsync(WebShopDbContext db, int count, ILogger logger)
    {
        if (await db.Customers.AnyAsync())
        {
            return;
        }

        List<ProductSeedEntry> productSnapshots = await db.Products
            .Select(p => new ProductSeedEntry(p.Id, p.Name, p.Price))
            .ToListAsync();

        Faker orderFaker = new();

        for (var i = 0; i < count; i++)
        {
            Faker faker   = new();
            var firstName = faker.Name.FirstName();
            var lastName  = faker.Name.LastName();

            Customer customer = new()
            {
                Id              = Guid.NewGuid(),
                FirstName       = firstName,
                LastName        = lastName,
                Email           = faker.Internet.Email(firstName, lastName),
                PhoneNumber     = faker.Phone.PhoneNumber(),
                ShippingAddress = faker.Address.FullAddress(),
            };

            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            var orderCount = faker.Random.Int(0, 6);
            for (var j = 0; j < orderCount; j++)
            {
                Order order = new()
                {
                    Id              = Guid.NewGuid(),
                    CustomerId      = customer.Id,
                    PlacedAt        = orderFaker.Date.Past(1).ToUniversalTime(),
                    Status          = orderFaker.PickRandom<OrderStatus>(),
                    ShippingAddress = customer.ShippingAddress ?? faker.Address.FullAddress(),
                };

                var lineCount = orderFaker.Random.Int(1, 5);
                IEnumerable<ProductSeedEntry> selected = orderFaker.PickRandom(productSnapshots, lineCount);

                foreach (ProductSeedEntry product in selected)
                {
                    order.Lines.Add(new OrderLine
                    {
                        Id          = Guid.NewGuid(),
                        ProductId   = product.Id,
                        ProductName = product.Name,
                        UnitPrice   = product.Price,
                        Quantity    = orderFaker.Random.Int(1, 5),
                    });
                }

                db.Orders.Add(order);
            }

            await db.SaveChangesAsync();
        }

        logger.LogInformation("{Count} customers created.", count);
    }

    /// <summary>Lightweight snapshot used during seeding to avoid reloading product data per order.</summary>
    private sealed record ProductSeedEntry(Guid Id, string Name, decimal Price);
}
