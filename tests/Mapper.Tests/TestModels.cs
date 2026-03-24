namespace ArchPillar.Extensions.Mapper.Tests;

// ---------------------------------------------------------------------------
// Source models (domain / entity layer)
// Hierarchy: User → Order → OrderLine → Product → Category  (5 levels)
// ---------------------------------------------------------------------------

public class User
{
    public required int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required UserRole Role { get; set; }
    public required Address Address { get; set; }
    public List<Order> Orders { get; set; } = [];
}

public class Address
{
    public required string Street { get; set; }
    public required string City { get; set; }
    public required string Country { get; set; }
    public string? PostalCode { get; set; }   // optional — not always present
}

public class Order
{
    public required int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public required OrderStatus Status { get; set; }
    public int OwnerId { get; set; }
    public required Customer Customer { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public class OrderLine
{
    public required int Id { get; set; }
    public required string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? SupplierName { get; set; }   // optional — not always loaded
    public Product? Product { get; set; }   // optional navigation to full product
}

public class Customer
{
    public required string Name { get; set; }
    public required string Email { get; set; }
}

public class Product
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public decimal ListPrice { get; set; }
    public ProductStatus Status { get; set; }
    public required Category Category { get; set; }
}

public class Category
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }   // optional
}

public enum OrderStatus { Pending, Shipped, Cancelled }
public enum UserRole { Guest, Member, Admin }
public enum ProductStatus { Active, Discontinued }

/// <summary>
/// Large enum (11 values) to exercise enum mapping with many members.
/// Deeply nested conditional expression trees are not translatable by
/// EF Core; the mapper must produce a flat switch expression instead.
/// </summary>
public enum PropertyType
{
    Invalid,
    Other,
    House,
    RowHouse,
    Apartment,
    Recreational,
    Cooperative,
    Farm,
    LandLeisure,
    LandResidence,
    HouseApartment,
}

/// <summary>
/// DTO counterpart of <see cref="PropertyType"/>.
/// Uses a different member order and omits some values to verify
/// non-trivial enum mapping (not a simple cast).
/// </summary>
public enum PropertyTypeDto
{
    Other,
    House,
    RowHouse,
    Apartment,
    Recreational,
    Cooperative,
    Farm,
    LandLeisure,
    LandResidence,
    HouseApartment,
    Invalid,
}

// ---------------------------------------------------------------------------
// Destination models (DTO / API layer)
// ---------------------------------------------------------------------------

public class UserDto
{
    public required int Id { get; set; }
    public required string FullName { get; set; }   // FirstName + " " + LastName
    public required string Email { get; set; }
    public required UserRoleDto Role { get; set; }
    public required AddressDto Address { get; set; }
    public List<OrderDto>? Orders { get; set; }         // optional — not always loaded
}

public class AddressDto
{
    public required string Street { get; set; }
    public required string City { get; set; }
    public required string Country { get; set; }
    public string? PostalCode { get; set; }   // optional
}

public class OrderDto
{
    public required int Id { get; set; }
    public required DateTime PlacedAt { get; set; }
    public required OrderStatusDto Status { get; set; }
    public required bool IsOwner { get; set; }
    public List<OrderLineDto> Lines { get; set; } = [];
    public string? CustomerName { get; set; }   // optional
}

public class OrderLineDto
{
    public required string ProductName { get; set; }
    public required int Quantity { get; set; }
    public required decimal UnitPrice { get; set; }
    public string? SupplierName { get; set; }   // optional
    public ProductDto? Product { get; set; }   // optional — full product detail
}

public class ProductDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required decimal ListPrice { get; set; }
    public required ProductStatusDto Status { get; set; }
    public required string CategoryName { get; set; }
    public string? CategoryDescription { get; set; }   // optional
}

public enum OrderStatusDto { Pending, Shipped, Cancelled }
public enum UserRoleDto { Guest, Member, Admin }
public enum ProductStatusDto { Active, Discontinued }

/// <summary>
/// Minimal entity for database-level enum mapping tests with a large enum.
/// </summary>
public class RealEstateProperty
{
    public required int Id { get; set; }
    public required string Label { get; set; }
    public required PropertyType Type { get; set; }
}

/// <summary>
/// DTO projection target for <see cref="RealEstateProperty"/>.
/// </summary>
public class RealEstatePropertyDto
{
    public required int Id { get; set; }
    public required string Label { get; set; }
    public required PropertyTypeDto Type { get; set; }
}

/// <summary>
/// Entity with an array of enum values — used to test enum array mapping
/// where each element is translated from one enum type to another.
/// </summary>
public class PropertyListing
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required List<PropertyType> Types { get; set; }
}

/// <summary>
/// DTO projection target for <see cref="PropertyListing"/>.
/// Each <see cref="PropertyType"/> element is mapped to <see cref="PropertyTypeDto"/>.
/// </summary>
public class PropertyListingDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required List<PropertyTypeDto> Types { get; set; }
}

// ---------------------------------------------------------------------------
// Models for NestedInlinerTests
// ---------------------------------------------------------------------------

// Issue 2: multiple mapper calls in one property expression (ternary)
public class FlexSource { public required bool UseFirst { get; set; } public required PartSource First  { get; set; } public required PartSource Second { get; set; } }
public class PartSource { public required string Text { get; set; } public string? Tag { get; set; } }
public class FlexDest   { public required PartDest Part { get; set; } }
public class PartDest   { public required string Label { get; set; } public string? Tag { get; set; } }

// Issue: Map() calls inside an inline new {} object initializer (not a mapper target type)
public class PackSource  { public required PartSource Primary { get; set; } public required PartSource Secondary { get; set; } }
public class PackDest    { public required PartDest   Primary { get; set; } public required PartDest   Secondary { get; set; } }
public class ShipmentSource { public required string Id { get; set; } public required PackSource Pack { get; set; } }
public class ShipmentDest   { public required string Id { get; set; } public required PackDest   Pack { get; set; } }

// Issue 3: nested mapper inside ToDictionary value selector
public class Catalog       { public required List<CatalogItem> Items { get; set; } }
public class CatalogItem   { public required string Key { get; set; } public required string Display { get; set; } }
public class CatalogDto    { public required Dictionary<string, CatalogItemDto> Items { get; set; } }
public class CatalogItemDto { public required string Label { get; set; } }

// Self-referencing types for circular reference detection
public class TreeNode    { public required string Name { get; set; } public TreeNode? Child { get; set; } }
public class TreeNodeDto { public required string Name { get; set; } public TreeNodeDto? Child { get; set; } }

// Destination with both parameterless and parameterized constructors
public class DualConstructorDto
{
    public string Name { get; set; } = "";
    public int Value { get; set; }

    public DualConstructorDto() { }

    public DualConstructorDto(string name, int value)
    {
        Name  = name;
        Value = value;
    }
}

// ---------------------------------------------------------------------------
// Models for ComposableMapperContextTests
// Demonstrates splitting mappings across multiple MapperContext instances.
// Domain: Author → Book → Publisher (cross-context references)
// ---------------------------------------------------------------------------

public class Author
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public List<Book> Books { get; set; } = [];
}

public class Book
{
    public required int Id { get; set; }
    public required string Title { get; set; }
    public required decimal Price { get; set; }
    public required Publisher Publisher { get; set; }
}

public class Publisher
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public string? Country { get; set; }
}

public class AuthorDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public List<BookDto>? Books { get; set; }
}

public class BookDto
{
    public required int Id { get; set; }
    public required string Title { get; set; }
    public required decimal Price { get; set; }
    public required PublisherDto Publisher { get; set; }
}

public class PublisherDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public string? Country { get; set; }
}
