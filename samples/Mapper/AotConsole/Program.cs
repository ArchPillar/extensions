using AotConsole;

var mapper = new AppMappers();

var order = new Order
{
    Id = 1,
    CustomerName = "Acme Corp",
    Total = 299.97m,
    Status = OrderStatus.Shipped,
    AssignedToUserId = 42,
    Items =
    [
        new OrderItem { ProductName = "Widget", Quantity = 3, UnitPrice = 49.99m },
        new OrderItem { ProductName = "Gadget", Quantity = 1, UnitPrice = 150.00m },
    ],
};

// Map without variable binding
var dto = mapper.Order.Map(order);
Console.WriteLine($"Order #{dto!.Id}: {dto.CustomerName} — {dto.Total:C}");
Console.WriteLine($"  Status: {dto.Status}");
Console.WriteLine($"  IsAssignedToMe (default): {dto.IsAssignedToMe}");
Console.WriteLine($"  Items: {dto.Items.Count}");
foreach (var item in dto.Items)
{
    Console.WriteLine($"    - {item.ProductName} x{item.Quantity} @ {item.UnitPrice:C}");
}

// Map with variable binding
var dtoWithVar = mapper.Order.Map(order, o => o.Set(mapper.CurrentUserId, 42));
Console.WriteLine();
Console.WriteLine($"  IsAssignedToMe (userId=42): {dtoWithVar!.IsAssignedToMe}");

// MapTo existing object
var existing = new OrderDto
{
    Id = 0,
    CustomerName = "",
    Total = 0,
    Status = OrderStatusDto.Pending,
    IsAssignedToMe = false,
};
mapper.Order.MapTo(order, existing, o => o.Set(mapper.CurrentUserId, 42));
Console.WriteLine();
Console.WriteLine($"MapTo result: #{existing.Id} {existing.CustomerName} — assigned={existing.IsAssignedToMe}");

// Null handling
var nullResult = mapper.Order.Map((Order?)null);
Console.WriteLine();
Console.WriteLine($"Null map result: {(nullResult is null ? "null (correct)" : "NOT null (bug)")}");

Console.WriteLine();
Console.WriteLine("AOT mapping completed successfully!");
