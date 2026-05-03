namespace ArchPillar.Extensions.Primitives.Tests;

public class UnwrapTests
{
    private sealed record Order(int Id);

    [Fact]
    public void Unwrap_OnSuccess_ReturnsValue()
    {
        var order = new Order(1);
        var result = OperationResult.Ok(order);

        Order unwrapped = result.Unwrap();

        Assert.Same(order, unwrapped);
    }

    [Fact]
    public void Unwrap_OnFailure_ThrowsOperationException()
    {
        OperationResult<Order> result = OperationResult.NotFound("missing");

        OperationException ex = Assert.Throws<OperationException>(() =>
        {
            result.Unwrap();
        });
        Assert.Equal(OperationStatus.NotFound, ex.Result.Status);
    }

    [Fact]
    public void Unwrap_BaseOnSuccess_DoesNotThrow()
    {
        var result = OperationResult.Ok();

        Exception? thrown = Record.Exception(() =>
        {
            result.Unwrap();
        });

        Assert.Null(thrown);
    }

    [Fact]
    public void Unwrap_BaseOnFailure_ThrowsOperationException()
    {
        OperationFailure result = OperationResult.Conflict("dup");

        OperationException ex = Assert.Throws<OperationException>(() =>
        {
            result.Unwrap();
        });
        Assert.Same(result, ex.Result);
    }

    [Fact]
    public async Task UnwrapAsync_OnTypedSuccess_ReturnsValueAsync()
    {
        var order = new Order(2);
        Task<OperationResult<Order>> task = OperationResult.Ok(order);

        Order unwrapped = await task.UnwrapAsync();

        Assert.Same(order, unwrapped);
    }

    [Fact]
    public async Task UnwrapAsync_OnTypedFailure_ThrowsAsync()
    {
        Task<OperationResult<Order>> task = (OperationResult<Order>)OperationResult.NotFound("missing");

        OperationException ex = await Assert.ThrowsAsync<OperationException>(() => task.UnwrapAsync());

        Assert.Equal(OperationStatus.NotFound, ex.Result.Status);
    }

    [Fact]
    public async Task UnwrapAsync_OnBaseSuccess_CompletesAsync()
    {
        Task<OperationResult> task = OperationResult.Ok();

        Exception? thrown = await Record.ExceptionAsync(() => task.UnwrapAsync());

        Assert.Null(thrown);
    }

    [Fact]
    public async Task UnwrapAsync_OnBaseFailure_ThrowsAsync()
    {
        Task<OperationResult> task = Task.FromResult<OperationResult>(OperationResult.Conflict("dup"));

        OperationException ex = await Assert.ThrowsAsync<OperationException>(() => task.UnwrapAsync());

        Assert.Equal(OperationStatus.Conflict, ex.Result.Status);
    }

    [Fact]
    public async Task UnwrapAsync_NullTask_ThrowsAsync()
    {
        Task<OperationResult>? task = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() => task!.UnwrapAsync());
    }
}
