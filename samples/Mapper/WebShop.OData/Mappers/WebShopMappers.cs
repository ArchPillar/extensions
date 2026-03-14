using ArchPillar.Extensions.Mapper;
using WebShop.OData.Models;
using WebShop.OData.Projections;

namespace WebShop.OData.Mappers;

/// <summary>
/// Central mapper context for the WebShop OData sample.
/// Every mapper is a named, IDE-navigable property.
/// </summary>
public sealed class WebShopMappers : MapperContext
{
    public Mapper<Category, CategoryProjection> Category { get; }

    public Mapper<Product, ProductProjection> Product { get; }

    public Mapper<OrderLine, OrderLineProjection> OrderLine { get; }

    public Mapper<Order, OrderProjection> Order { get; }

    public Mapper<Customer, CustomerProjection> Customer { get; }

    public WebShopMappers()
    {
        // Leaf mappers first — no dependencies on other mappers.

        Category = CreateMapper<Category, CategoryProjection>(src => new CategoryProjection
        {
            Id           = src.Id,
            Name         = src.Name,
            Description  = src.Description,
            ProductCount = src.Products.Count(),
        }).Build();

        Product = CreateMapper<Product, ProductProjection>(src => new ProductProjection
        {
            Id            = src.Id,
            Name          = src.Name,
            Description   = src.Description,
            Price         = src.Price,
            StockQuantity = src.StockQuantity,
            CategoryName  = src.Category.Name,
            IsAvailable   = src.IsActive && src.StockQuantity > 0,
        }).Build();

        OrderLine = CreateMapper<OrderLine, OrderLineProjection>(src => new OrderLineProjection
        {
            Id          = src.Id,
            ProductName = src.ProductName,
            Quantity    = src.Quantity,
            UnitPrice   = src.UnitPrice,
            LineTotal   = src.Quantity * src.UnitPrice,
        }).Build();

        // Order references OrderLine (nested collection).
        Order = CreateMapper<Order, OrderProjection>(src => new OrderProjection
        {
            Id               = src.Id,
            PlacedAt         = src.PlacedAt,
            Status           = src.Status.ToString(),
            TotalAmount      = src.Lines.Sum(l => l.Quantity * l.UnitPrice),
            LineCount        = src.Lines.Count(),
            CustomerFullName = src.Customer.FirstName + " " + src.Customer.LastName,
        })
        .Optional(p => p.Lines, src => src.Lines.Project(OrderLine))
        .Build();

        // Customer computes aggregate spend.
        Customer = CreateMapper<Customer, CustomerProjection>(src => new CustomerProjection
        {
            Id          = src.Id,
            FullName    = src.FirstName + " " + src.LastName,
            Email       = src.Email,
            PhoneNumber = src.PhoneNumber,
            TotalOrders = src.Orders.Count(),
            TotalSpent  = src.Orders.Sum(o => o.Lines.Sum(l => l.Quantity * l.UnitPrice)),
        }).Build();

        EagerBuildAll();
    }
}
