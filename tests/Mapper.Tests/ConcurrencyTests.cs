namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Validates that shared mapper instances are safe for concurrent use
/// from multiple threads. Mappers use <see cref="Lazy{T}"/> for compiled
/// delegates, so concurrent first-access and steady-state usage must both
/// produce correct results without data races.
/// </summary>
public class ConcurrencyTests
{
    private const int ThreadCount = 50;

    // -----------------------------------------------------------------------
    // Concurrent Map — cold start (first access triggers compilation)
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_ConcurrentColdStart_ProducesCorrectResults()
    {
        // Fresh context — no mapper has been compiled yet.
        // All threads race to trigger the first Lazy<T> evaluation.
        var mappers = new TestMappers();
        Order source = CreateTestOrder(ownerId: 1);

        OrderDto?[] results = RunConcurrently(_ => mappers.Order.Map(source));

        foreach (OrderDto? result in results)
        {
            Assert.NotNull(result);
            Assert.Equal(source.Id, result.Id);
            Assert.Equal(source.Lines.Count, result.Lines.Count);
        }
    }

    // -----------------------------------------------------------------------
    // Concurrent Map — warm (pre-compiled via EagerBuildAll)
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_ConcurrentWarmStart_ProducesCorrectResults()
    {
        var mappers = new EagerTestMappers();
        Order source = CreateTestOrder(ownerId: 1);

        OrderDto?[] results = RunConcurrently(_ => mappers.Order.Map(source));

        foreach (OrderDto? result in results)
        {
            Assert.NotNull(result);
            Assert.Equal(source.Id, result.Id);
            Assert.Equal(source.Lines.Count, result.Lines.Count);
        }
    }

    // -----------------------------------------------------------------------
    // Concurrent Map with per-thread variable bindings
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_ConcurrentWithVariables_EachThreadSeesOwnBinding()
    {
        var mappers = new EagerTestMappers();

        OrderDto?[] results = RunConcurrently(threadIndex =>
        {
            Order source = CreateTestOrder(ownerId: threadIndex);
            return mappers.Order.Map(source, o => o.Set(mappers.CurrentUserId, threadIndex));
        });

        for (var i = 0; i < ThreadCount; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal(i, results[i]!.Id);
            Assert.True(results[i]!.IsOwner);
        }
    }

    // -----------------------------------------------------------------------
    // Concurrent ToExpression
    // -----------------------------------------------------------------------

    [Fact]
    public void ToExpression_ConcurrentAccess_ProducesValidExpressions()
    {
        var mappers = new EagerTestMappers();
        Order source = CreateTestOrder(ownerId: 1);

        OrderDto?[] results = RunConcurrently(_ =>
        {
            var expression = mappers.Order.ToExpression();
            Func<Order, OrderDto> compiled = expression.Compile();
            return compiled(source);
        });

        foreach (OrderDto? result in results)
        {
            Assert.NotNull(result);
            Assert.Equal(source.Id, result.Id);
        }
    }

    // -----------------------------------------------------------------------
    // Concurrent MapTo
    // -----------------------------------------------------------------------

    [Fact]
    public void MapTo_ConcurrentAccess_ProducesCorrectResults()
    {
        var mappers = new EagerTestMappers();

        OrderDto?[] results = RunConcurrently(threadIndex =>
        {
            Order source = CreateTestOrder(ownerId: threadIndex);
            var destination = new OrderDto
            {
                Id = 0,
                PlacedAt = default,
                Status = OrderStatusDto.Pending,
                IsOwner = false,
                Lines = [],
            };
            mappers.Order.MapTo(source, destination, o => o.Set(mappers.CurrentUserId, threadIndex));
            return destination;
        });

        for (var i = 0; i < ThreadCount; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal(i, results[i]!.Id);
            Assert.True(results[i]!.IsOwner);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static T?[] RunConcurrently<T>(Func<int, T?> action)
    {
        var results = new T?[ThreadCount];
        using var barrier = new Barrier(ThreadCount);

        Thread[] threads = Enumerable.Range(0, ThreadCount).Select(i =>
        {
            var thread = new Thread(() =>
            {
                barrier.SignalAndWait();
                results[i] = action(i);
            });
            return thread;
        }).ToArray();

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        return results;
    }

    private static Order CreateTestOrder(int ownerId)
    {
        return new Order
        {
            Id = ownerId,
            Status = OrderStatus.Pending,
            OwnerId = ownerId,
            Customer = new Customer { Name = $"Customer{ownerId}", Email = $"c{ownerId}@test.com" },
            Lines =
            [
                new OrderLine
                {
                    Id = 1,
                    ProductName = "Widget",
                    Quantity = 3,
                    UnitPrice = 9.99m,
                },
            ],
        };
    }
}
