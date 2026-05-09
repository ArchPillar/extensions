namespace Command.HostSample.Orders;

internal sealed class InMemoryOrderStore
{
    private readonly Dictionary<Guid, (string Customer, int Quantity)> _orders = [];

    public Guid Create(string customer, int quantity)
    {
        var id = Guid.NewGuid();
        _orders[id] = (customer, quantity);
        return id;
    }

    public bool TryGet(Guid id, out (string Customer, int Quantity) order)
        => _orders.TryGetValue(id, out order);

    public bool Remove(Guid id) => _orders.Remove(id);
}
