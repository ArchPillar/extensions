namespace ArchPillar.Extensions.Primitives.Tests;

public class UnwrapTests
{
    private sealed record Order(int Id);

    [Fact]
    public void Unwrap_OnSuccess_ReturnsValue()
    {
        var order = new Order(1);
        var result = OperationResult<Order>.Ok(order);

        Order unwrapped = result.Unwrap();

        Assert.Same(order, unwrapped);
    }

    [Fact]
    public void Unwrap_OnFailure_ThrowsOperationException()
    {
        var result = OperationResult<Order>.NotFound("missing");

        OperationException ex = Assert.Throws<OperationException>(() =>
        {
            result.Unwrap();
        });
        Assert.Same(result, ex.Result);
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
        var result = OperationResult.Conflict("dup");

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

        Order unwrapped = await Task.FromResult(OperationResult<Order>.Ok(order)).UnwrapAsync();

        Assert.Same(order, unwrapped);
    }

    [Fact]
    public async Task UnwrapAsync_OnTypedFailure_ThrowsAsync()
    {
        OperationException ex = await Assert.ThrowsAsync<OperationException>(() =>
            Task.FromResult(OperationResult<Order>.NotFound("missing")).UnwrapAsync());
        Assert.Equal(OperationStatus.NotFound, ex.Result.Status);
    }

    [Fact]
    public async Task UnwrapAsync_OnBaseSuccess_CompletesAsync()
    {
        Exception? thrown = await Record.ExceptionAsync(() =>
            Task.FromResult(OperationResult.Ok()).UnwrapAsync());

        Assert.Null(thrown);
    }

    [Fact]
    public async Task UnwrapAsync_OnBaseFailure_ThrowsAsync()
    {
        OperationException ex = await Assert.ThrowsAsync<OperationException>(() =>
            Task.FromResult(OperationResult.Conflict("dup")).UnwrapAsync());
        Assert.Equal(OperationStatus.Conflict, ex.Result.Status);
    }

    [Fact]
    public async Task UnwrapAsync_NullTask_ThrowsAsync()
    {
        Task<OperationResult>? task = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() => task!.UnwrapAsync());
    }
}
