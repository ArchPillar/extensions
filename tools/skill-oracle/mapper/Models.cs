namespace MapTest;

public enum OrderStatus { Pending, Shipped, Cancelled }
public enum OrderStatusDto { Pending, Shipped, Cancelled }
public enum Priority { Low, Medium, High, Critical }
public enum PriorityBucketDto { Normal, Urgent }

public class Customer { public string Name { get; set; } = ""; }
public class Product  { public string Name { get; set; } = ""; }

public class OrderLine
{
    public int Id { get; set; }
    public Product Product { get; set; } = new();
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public OrderStatus Status { get; set; }
    public Priority Priority { get; set; }
    public int OwnerId { get; set; }
    public Customer Customer { get; set; } = new();
    public List<OrderLine> Lines { get; set; } = new();
    public bool IsActive { get; set; }
}

public class OrderLineDto
{
    public required int Id { get; set; }
    public required string ProductName { get; set; }
    public required int Quantity { get; set; }
    public required decimal UnitPrice { get; set; }
}

public class OrderDto
{
    public required int Id { get; set; }
    public required OrderStatusDto Status { get; set; }
    public required List<OrderLineDto> Lines { get; set; }
    public bool IsOwner { get; set; }
    public string? CustomerName { get; set; }
}

public class Item { public string Sku { get; set; } = ""; public string Name { get; set; } = ""; }
public class ItemDto { public required string Sku { get; set; } public required string Name { get; set; } }
public class Catalog { public int Id { get; set; } public Dictionary<string, Item> Items { get; set; } = new(); }
public class CatalogDto { public required int Id { get; set; } public required Dictionary<string, ItemDto> Items { get; set; } }
