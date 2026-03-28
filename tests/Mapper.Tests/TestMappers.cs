namespace ArchPillar.Extensions.Mapper.Tests;

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
    public EnumMapper<PropertyType, PropertyTypeDto> PropertyTypeMapper { get; }

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
        PropertyTypeMapper = CreateEnumMapper<PropertyType, PropertyTypeDto>(MapPropertyType);

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

    private static PropertyTypeDto MapPropertyType(PropertyType type) => type switch
    {
        PropertyType.Invalid => PropertyTypeDto.Other,
        PropertyType.Other => PropertyTypeDto.Other,
        PropertyType.House => PropertyTypeDto.House,
        PropertyType.RowHouse => PropertyTypeDto.RowHouse,
        PropertyType.Apartment => PropertyTypeDto.Apartment,
        PropertyType.Recreational => PropertyTypeDto.Recreational,
        PropertyType.Cooperative => PropertyTypeDto.Cooperative,
        PropertyType.Farm => PropertyTypeDto.Farm,
        PropertyType.LandLeisure => PropertyTypeDto.LandLeisure,
        PropertyType.LandResidence => PropertyTypeDto.LandResidence,
        PropertyType.HouseApartment => PropertyTypeDto.HouseApartment,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
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
/// Mappers exercising the three nullable enum scenarios.
/// </summary>
public class NullableEnumMappers : MapperContext
{
    public EnumMapper<OrderStatus, OrderStatusDto> OrderStatusMapper { get; }

    /// <summary>Nullable → nullable: <c>Map(TSource?)</c> → <c>TDest?</c>.</summary>
    public Mapper<OrderWithNullableStatus, OrderDtoWithNullableStatus> NullableToNullable { get; }

    /// <summary>Nullable → non-nullable with default: <c>Map(TSource?, TDest)</c> → <c>TDest</c>.</summary>
    public Mapper<OrderWithNullableStatus, OrderDtoWithDefaultStatus> NullableToNonNullable { get; }

    /// <summary>Non-nullable → nullable: implicit lift via C# assignment.</summary>
    public Mapper<Order, OrderDtoWithNullableStatus> NonNullableToNullable { get; }

    public NullableEnumMappers()
    {
        OrderStatusMapper = CreateEnumMapper<OrderStatus, OrderStatusDto>(status => status switch
        {
            OrderStatus.Pending => OrderStatusDto.Pending,
            OrderStatus.Shipped => OrderStatusDto.Shipped,
            OrderStatus.Cancelled => OrderStatusDto.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        });

        NullableToNullable = CreateMapper<OrderWithNullableStatus, OrderDtoWithNullableStatus>(
            src => new OrderDtoWithNullableStatus
            {
                Id     = src.Id,
                Status = OrderStatusMapper.Map(src.Status),
            });

        NullableToNonNullable = CreateMapper<OrderWithNullableStatus, OrderDtoWithDefaultStatus>(
            src => new OrderDtoWithDefaultStatus
            {
                Id     = src.Id,
                Status = OrderStatusMapper.Map(src.Status, OrderStatusDto.Pending),
            });

        NonNullableToNullable = CreateMapper<Order, OrderDtoWithNullableStatus>(
            src => new OrderDtoWithNullableStatus
            {
                Id     = src.Id,
                Status = OrderStatusMapper.Map(src.Status),
            });
    }
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

// ---------------------------------------------------------------------------
// Composable MapperContext examples — cross-context dependency injection
// ---------------------------------------------------------------------------

/// <summary>
/// Owns the Publisher mapping. Has no dependencies on other contexts.
/// In a real application, this would be registered as a singleton in the DI container.
/// </summary>
public class PublisherMappers : MapperContext
{
    public Mapper<Publisher, PublisherDto> Publisher { get; }

    public PublisherMappers()
    {
        Publisher = CreateMapper<Publisher, PublisherDto>(src => new PublisherDto
        {
            Id   = src.Id,
            Name = src.Name,
        })
        .Optional(dest => dest.Country, src => src.Country);
    }
}

/// <summary>
/// Owns the Book mapping. Depends on <see cref="PublisherMappers"/> for the
/// nested Publisher → PublisherDto mapping. Receives the dependency via
/// constructor injection.
/// </summary>
public class BookMappers : MapperContext
{
    public Mapper<Book, BookDto> Book { get; }

    public BookMappers(PublisherMappers publisherMappers)
    {
        Book = CreateMapper<Book, BookDto>(src => new BookDto
        {
            Id        = src.Id,
            Title     = src.Title,
            Price     = src.Price,
            Publisher = publisherMappers.Publisher.Map(src.Publisher),
        });
    }
}

/// <summary>
/// Owns the Author mapping. Depends on <see cref="BookMappers"/> for the
/// nested Book → BookDto collection projection. Receives the dependency via
/// constructor injection.
/// </summary>
public class AuthorMappers : MapperContext
{
    public Mapper<Author, AuthorDto> Author { get; }

    public AuthorMappers(BookMappers bookMappers)
    {
        Author = CreateMapper<Author, AuthorDto>(src => new AuthorDto
        {
            Id   = src.Id,
            Name = src.Name,
        })
        .Optional(dest => dest.Books, src => src.Books.Project(bookMappers.Book).ToList());
    }
}

/// <summary>
/// Aggregates multiple <see cref="MapperContext"/> subclasses into a single
/// injectable facade. This is a plain C# class — not a <see cref="MapperContext"/>
/// itself — that composes the individual contexts.
/// </summary>
public class CompositeMappers(PublisherMappers publishers, BookMappers books, AuthorMappers authors)
{
    public PublisherMappers Publishers { get; } = publishers;
    public BookMappers Books { get; } = books;
    public AuthorMappers Authors { get; } = authors;
}

// ---------------------------------------------------------------------------
// Method-based mapper composition — mappers retrieved via method calls
// ---------------------------------------------------------------------------

/// <summary>
/// Demonstrates mapper composition where nested mappers are retrieved via
/// method calls (no arguments) instead of property access.
/// </summary>
public class MethodBasedBookMappers : MapperContext
{
    private readonly PublisherMappers _publisherMappers;

    public Mapper<Book, BookDto> Book { get; }

    public MethodBasedBookMappers(PublisherMappers publisherMappers)
    {
        _publisherMappers = publisherMappers;

        Book = CreateMapper<Book, BookDto>(src => new BookDto
        {
            Id        = src.Id,
            Title     = src.Title,
            Price     = src.Price,
            Publisher = GetPublisherMapper().Map(src.Publisher),
        });
    }

    public Mapper<Publisher, PublisherDto> GetPublisherMapper()
        => _publisherMappers.Publisher;
}

/// <summary>
/// Demonstrates mapper composition where nested mappers are retrieved via
/// method calls with constant arguments.
/// </summary>
public class ConstArgMethodBookMappers : MapperContext
{
    private readonly Dictionary<string, object> _mapperRegistry = new();

    public Mapper<Book, BookDto> Book { get; }

    public ConstArgMethodBookMappers(PublisherMappers publisherMappers)
    {
        _mapperRegistry["publisher"] = publisherMappers.Publisher;

        Book = CreateMapper<Book, BookDto>(src => new BookDto
        {
            Id        = src.Id,
            Title     = src.Title,
            Price     = src.Price,
            Publisher = GetMapper<Publisher, PublisherDto>("publisher").Map(src.Publisher),
        });
    }

    public Mapper<TSource, TDest> GetMapper<TSource, TDest>(string name)
        => (Mapper<TSource, TDest>)_mapperRegistry[name];
}

/// <summary>
/// Eager-build variant of <see cref="MethodBasedBookMappers"/> — validates that
/// <see cref="MapperContext.EagerBuildAll"/> discovers mappers returned by
/// parameterless methods.
/// </summary>
public class EagerMethodBasedBookMappers : MapperContext
{
    private readonly PublisherMappers _publisherMappers;

    public Mapper<Book, BookDto> Book { get; }

    public EagerMethodBasedBookMappers(PublisherMappers publisherMappers)
    {
        _publisherMappers = publisherMappers;

        Book = CreateMapper<Book, BookDto>(src => new BookDto
        {
            Id        = src.Id,
            Title     = src.Title,
            Price     = src.Price,
            Publisher = GetPublisherMapper().Map(src.Publisher),
        });

        EagerBuildAll();
    }

    public Mapper<Publisher, PublisherDto> GetPublisherMapper()
        => _publisherMappers.Publisher;
}

/// <summary>
/// Demonstrates collection projection with mapper retrieved via method call.
/// </summary>
public class MethodBasedAuthorMappers : MapperContext
{
    private readonly BookMappers _bookMappers;

    public Mapper<Author, AuthorDto> Author { get; }

    public MethodBasedAuthorMappers(BookMappers bookMappers)
    {
        _bookMappers = bookMappers;

        Author = CreateMapper<Author, AuthorDto>(src => new AuthorDto
        {
            Id   = src.Id,
            Name = src.Name,
        })
        .Optional(dest => dest.Books, src => src.Books.Project(GetBookMapper()).ToList());
    }

    public Mapper<Book, BookDto> GetBookMapper()
        => _bookMappers.Book;
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

// ---------------------------------------------------------------------------
// Mapper inheritance — destination type hierarchy
// ---------------------------------------------------------------------------

/// <summary>
/// Demonstrates mapper inheritance: a base mapper for Document → DocumentSummaryDto,
/// with two derived mappers inheriting the base mappings and extending them
/// for DocumentDetailDto and DocumentStatsDto.
/// </summary>
public class InheritanceMappers : MapperContext
{
    public Mapper<Document, DocumentSummaryDto> Summary { get; }
    public Mapper<Document, DocumentDetailDto>  Detail  { get; }
    public Mapper<Document, DocumentStatsDto>   Stats   { get; }

    public InheritanceMappers()
    {
        Summary = CreateMapper<Document, DocumentSummaryDto>(src => new DocumentSummaryDto
        {
            Id     = src.Id,
            Title  = src.Title,
            Author = src.Author,
        })
        .Optional(dest => dest.Category, src => src.Category);

        Detail = Inherit(Summary).For<DocumentDetailDto>()
            .Map(dest => dest.Content, src => src.Content)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt)
            .Optional(dest => dest.ReviewerName, src => src.ReviewedBy.Name);

        Stats = Inherit(Summary).For<DocumentStatsDto>()
            .Map(dest => dest.ViewCount, src => src.ViewCount);
    }
}

/// <summary>
/// Demonstrates mapper inheritance with both source and destination derived types.
/// TechnicalDocument extends Document, TechnicalDocumentDto extends DocumentDetailDto.
/// </summary>
public class DerivedSourceMappers : MapperContext
{
    public Mapper<Document, DocumentSummaryDto>               Summary   { get; }
    public Mapper<Document, DocumentDetailDto>                Detail    { get; }
    public Mapper<TechnicalDocument, TechnicalDocumentDto>    Technical { get; }

    public DerivedSourceMappers()
    {
        Summary = CreateMapper<Document, DocumentSummaryDto>(src => new DocumentSummaryDto
        {
            Id     = src.Id,
            Title  = src.Title,
            Author = src.Author,
        });

        Detail = Inherit(Summary).For<DocumentDetailDto>()
            .Map(dest => dest.Content, src => src.Content)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt);

        Technical = Inherit(Detail).For<TechnicalDocument, TechnicalDocumentDto>()
            .Map(dest => dest.Language, src => src.Language);
    }
}

/// <summary>
/// Eager-build variant of <see cref="InheritanceMappers"/> — validates that
/// inherited mappers compile successfully at startup.
/// </summary>
public class EagerInheritanceMappers : InheritanceMappers
{
    public EagerInheritanceMappers() { EagerBuildAll(); }
}

// ---------------------------------------------------------------------------
// Collection MapTo mode test mappers
// ---------------------------------------------------------------------------

/// <summary>
/// Uses <see cref="CollectionMapToMode.Deep"/> for the Order → OrderDto Lines
/// collection. MapTo preserves the collection instance (clear + re-add).
/// </summary>
public class DeepCollectionMappers : MapperContext
{
    public EnumMapper<OrderStatus, OrderStatusDto> OrderStatusMapper { get; }
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();
    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }
    public Mapper<Order, OrderDto> Order { get; }

    public DeepCollectionMappers()
    {
        OrderStatusMapper = CreateEnumMapper<OrderStatus, OrderStatusDto>(s => s switch
        {
            OrderStatus.Pending => OrderStatusDto.Pending,
            OrderStatus.Shipped => OrderStatusDto.Shipped,
            OrderStatus.Cancelled => OrderStatusDto.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
        });

        OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
        {
            ProductName = src.ProductName,
            Quantity    = src.Quantity,
            UnitPrice   = src.UnitPrice,
        });

        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id       = src.Id,
            PlacedAt = src.CreatedAt,
            Status   = OrderStatusMapper.Map(src.Status),
            IsOwner  = src.OwnerId == CurrentUserId,
            Lines    = src.Lines.Project(OrderLine).ToList(),
        })
        .Optional(dest => dest.CustomerName, src => src.Customer.Name)
        .MapToCollection<OrderLineDto>(dest => dest.Lines, CollectionMapToMode.Deep);
    }
}

/// <summary>
/// Uses <see cref="CollectionMapToMode.DeepWithIdentity"/> for the
/// TaskBoard → TaskBoardDto Items collection. MapTo preserves both the
/// collection instance and individual element instances matched by Id.
/// </summary>
public class IdentityCollectionMappers : MapperContext
{
    public Mapper<TaskItem, TaskItemDto> TaskItem { get; }
    public Mapper<TaskBoard, TaskBoardDto> TaskBoard { get; }

    public IdentityCollectionMappers()
    {
        TaskItem = CreateMapper<TaskItem, TaskItemDto>(src => new TaskItemDto
        {
            Id    = src.Id,
            Title = src.Title,
            Done  = src.Done,
        });

        TaskBoard = CreateMapper<TaskBoard, TaskBoardDto>(src => new TaskBoardDto
        {
            Id    = src.Id,
            Items = src.Items.Project(TaskItem).ToList(),
        })
        .MapToCollection(
            dest => dest.Items,
            src => src.Items,
            TaskItem,
            src => src.Id,
            dest => dest.Id);
    }
}
