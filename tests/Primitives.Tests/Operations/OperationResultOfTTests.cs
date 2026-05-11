namespace ArchPillar.Extensions.Operations;

public class OperationResultOfTTests
{
    private sealed record Order(int Id, string Customer);

    [Fact]
    public void Ok_GenericInferred_CarriesValue()
    {
        var order = new Order(1, "alice");

        var result = OperationResult.Ok(order);

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Same(order, result.Value);
        Assert.Null(result.Problem);
    }

    [Fact]
    public void Created_GenericInferred_HasStatusCreated()
    {
        var order = new Order(2, "bob");

        var result = OperationResult.Created(order);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Same(order, result.Value);
    }

    [Fact]
    public void NotFound_ImplicitConvertsToTypedResult()
    {
        // Failure factory returns OperationFailure, implicit conversion lifts to typed result.
        OperationResult<Order> result = OperationResult.NotFound("missing");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(OperationStatus.NotFound, result.Status);
        Assert.NotNull(result.Problem);
        Assert.Equal("missing", result.Problem!.Detail);
    }

    [Fact]
    public void ImplicitFromValue_ProducesOkResult()
    {
        OperationResult<Order> result = new Order(3, "carol");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(3, result.Value!.Id);
    }

    [Fact]
    public async Task ImplicitTaskConversion_WrapsSynchronouslyAsync()
    {
        Task<OperationResult<Order>> task = OperationResult.Ok(new Order(4, "d"));

        Assert.True(task.IsCompletedSuccessfully);
        OperationResult<Order> awaited = await task;
        Assert.True(awaited.IsSuccess);
    }

    [Fact]
    public void ImplicitExceptionConversion_StillWorksOnGeneric()
    {
        OperationResult<Order> source = OperationResult.Conflict("conflict");

        Exception ex = source;

        OperationException op = Assert.IsType<OperationException>(ex);
        Assert.Equal(OperationStatus.Conflict, op.Result.Status);
    }
}
