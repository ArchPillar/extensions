namespace AotConsole;

// Source models
public class Order
{
    public required int Id { get; set; }
    public required string CustomerName { get; set; }
    public required decimal Total { get; set; }
    public required OrderStatus Status { get; set; }
    public int AssignedToUserId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public required string ProductName { get; set; }
    public required int Quantity { get; set; }
    public required decimal UnitPrice { get; set; }
}

public enum OrderStatus { Pending, Processing, Shipped, Delivered }

// Destination models
public class OrderDto
{
    public required int Id { get; set; }
    public required string CustomerName { get; set; }
    public required decimal Total { get; set; }
    public required OrderStatusDto Status { get; set; }
    public required bool IsAssignedToMe { get; set; }
    public List<OrderItemDto> Items { get; set; } = [];
}

public class OrderItemDto
{
    public required string ProductName { get; set; }
    public required int Quantity { get; set; }
    public required decimal UnitPrice { get; set; }
}

public enum OrderStatusDto { Pending, Processing, Shipped, Delivered }
