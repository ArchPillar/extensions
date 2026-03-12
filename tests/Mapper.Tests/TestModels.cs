namespace ArchPillar.Mapper.Tests;

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
