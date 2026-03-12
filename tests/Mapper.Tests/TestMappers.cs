namespace ArchPillar.Mapper.Tests;

/// <summary>
/// The canonical test mapper context used across all test files.
/// Mirrors the real-world usage pattern shown in the spec.
/// <para>
/// Model hierarchy: User → Order → OrderLine → Product → Category  (5 levels)
/// </para>
/// <para>
/// Note: enum mapper properties are named with a "Mapper" suffix
/// (e.g. <c>OrderStatusMapper</c>) to avoid naming collisions with the
/// enum types of the same name inside the same namespace.
/// </para>
/// </summary>
public class TestMappers : MapperContext
{
    // Runtime variable — referenced by name, no magic strings
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

    // Enum mappers
    public EnumMapper<OrderStatus, OrderStatusDto> OrderStatusMapper { get; }
    public EnumMapper<UserRole, UserRoleDto> UserRoleMapper { get; }
    public EnumMapper<ProductStatus, ProductStatusDto> ProductStatusMapper { get; }

    // Value-object / leaf mapper
    public Mapper<Address, AddressDto> Address { get; }

    // Level 4 → 5: Product (with inlined Category fields)
    public Mapper<Product, ProductDto> Product { get; }

    // Level 3: OrderLine (optional: SupplierName, Product)
    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }

    // Level 2: Order (optional: CustomerName)
    public Mapper<Order, OrderDto> Order { get; }

    // Level 1: User (optional: Orders)
    public Mapper<User, UserDto> User { get; }

    public TestMappers()
    {
        OrderStatusMapper = CreateEnumMapper<OrderStatus, OrderStatusDto>(MapOrderStatus);
        UserRoleMapper = CreateEnumMapper<UserRole, UserRoleDto>(MapUserRole);
        ProductStatusMapper = CreateEnumMapper<ProductStatus, ProductStatusDto>(MapProductStatus);

        Address = CreateMapper<Address, AddressDto>(src => new AddressDto
        {
            Street = src.Street,
            City = src.City,
            Country = src.Country,
        })
        .Optional(dest => dest.PostalCode, src => src.PostalCode);

        Product = CreateMapper<Product, ProductDto>(src => new ProductDto
        {
            Id = src.Id,
            Name = src.Name,
            ListPrice = src.ListPrice,
            Status = ProductStatusMapper.Map(src.Status),
            CategoryName = src.Category.Name,
        })
        .Optional(dest => dest.CategoryDescription, src => src.Category.Description);

        OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
        {
            ProductName = src.ProductName,
            Quantity = src.Quantity,
            UnitPrice = src.UnitPrice,
        })
        .Optional(dest => dest.SupplierName, src => src.SupplierName)
        .Optional(dest => dest.Product, src => Product.Map(src.Product));

        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id = src.Id,
            PlacedAt = src.CreatedAt,
            Status = OrderStatusMapper.Map(src.Status),
            IsOwner = src.OwnerId == CurrentUserId,
            Lines = src.Lines.Project(OrderLine).ToList(),
        })
        .Optional(dest => dest.CustomerName, src => src.Customer.Name);

        User = CreateMapper<User, UserDto>(src => new UserDto
        {
            Id = src.Id,
            FullName = src.FirstName + " " + src.LastName,
            Email = src.Email,
            Role = UserRoleMapper.Map(src.Role),
            Address = Address.Map(src.Address),
        })
        .Optional(dest => dest.Orders, src => src.Orders.Project(Order).ToList());
    }

    private static OrderStatusDto MapOrderStatus(OrderStatus status) => status switch
    {
        OrderStatus.Pending => OrderStatusDto.Pending,
        OrderStatus.Shipped => OrderStatusDto.Shipped,
        OrderStatus.Cancelled => OrderStatusDto.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static UserRoleDto MapUserRole(UserRole role) => role switch
    {
        UserRole.Guest => UserRoleDto.Guest,
        UserRole.Member => UserRoleDto.Member,
        UserRole.Admin => UserRoleDto.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    private static ProductStatusDto MapProductStatus(ProductStatus status) => status switch
    {
        ProductStatus.Active => ProductStatusDto.Active,
        ProductStatus.Discontinued => ProductStatusDto.Discontinued,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}

/// <summary>
/// Eager-build variant — all mappers compile at construction time.
/// Used in build-validation and benchmark tests.
/// </summary>
public class EagerTestMappers : TestMappers
{
    public EagerTestMappers() { EagerBuildAll(); }
}

/// <summary>
/// Declares Order BEFORE OrderLine — the reverse of dependency order.
/// Verifies that deferred nested-mapper resolution allows any declaration order.
/// </summary>
public class ReverseOrderMappers : MapperContext
{
    public EnumMapper<OrderStatus, OrderStatusDto> OrderStatusMapper { get; }
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

    // Order is declared FIRST, but references OrderLine which is declared AFTER.
    // The properties are null at construction time but resolved lazily on first use.
    public Mapper<Order, OrderDto> Order { get; private set; } = null!;
    public Mapper<OrderLine, OrderLineDto> OrderLine { get; private set; } = null!;

    public ReverseOrderMappers()
    {
        OrderStatusMapper = CreateEnumMapper<OrderStatus, OrderStatusDto>(s => s switch
        {
            OrderStatus.Pending => OrderStatusDto.Pending,
            OrderStatus.Shipped => OrderStatusDto.Shipped,
            OrderStatus.Cancelled => OrderStatusDto.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
        });

        // Order references OrderLine.Project() — but OrderLine is not assigned yet!
        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id = src.Id,
            PlacedAt = src.CreatedAt,
            Status = OrderStatusMapper.Map(src.Status),
            IsOwner = src.OwnerId == CurrentUserId,
            Lines = src.Lines.Project(OrderLine).ToList(),
        })
        .Optional(dest => dest.CustomerName, src => src.Customer.Name);

        // OrderLine is assigned AFTER Order.
        OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
        {
            ProductName = src.ProductName,
            Quantity = src.Quantity,
            UnitPrice = src.UnitPrice,
        })
        .Optional(dest => dest.SupplierName, src => src.SupplierName);
    }
}

/// <summary>
/// Mappers for <see cref="NestedInlinerTests"/>: exercises multiple nested
/// mapper calls per property (issue 2) and <c>ToDictionary</c> with a
/// nested mapper in the value selector (issue 3).
/// </summary>
public class InlinerTestMappers : MapperContext
{
    // Issue 2 — two Map() calls in one ternary expression
    public Mapper<PartSource, PartDest> Part { get; }
    public Mapper<FlexSource, FlexDest> Flex { get; }

    // Issue 3 — Map() inside a ToDictionary value selector
    public Mapper<CatalogItem, CatalogItemDto> CatalogItem { get; }
    public Mapper<Catalog, CatalogDto>         Catalog     { get; }

    // Map() calls inside an inline new {} initializer (PackDest is not a mapper target)
    public Mapper<ShipmentSource, ShipmentDest> Shipment { get; }

    public InlinerTestMappers()
    {
        Part = CreateMapper<PartSource, PartDest>(s => new PartDest
        {
            Label = s.Text,
        })
        .Optional(dest => dest.Tag, src => src.Tag);

        Flex = CreateMapper<FlexSource, FlexDest>(s => new FlexDest
        {
            Part = s.UseFirst ? Part.Map(s.First) : Part.Map(s.Second),
        });

        CatalogItem = CreateMapper<CatalogItem, CatalogItemDto>(i => new CatalogItemDto
        {
            Label = i.Display,
        });

        Catalog = CreateMapper<Catalog, CatalogDto>(c => new CatalogDto
        {
            Items = c.Items.ToDictionary(i => i.Key, i => CatalogItem.Map(i)),
        });

        Shipment = CreateMapper<ShipmentSource, ShipmentDest>(s => new ShipmentDest
        {
            Id   = s.Id,
            Pack = new PackDest
            {
                Primary   = Part.Map(s.Pack.Primary),
                Secondary = Part.Map(s.Pack.Secondary),
            },
        });
    }
}
