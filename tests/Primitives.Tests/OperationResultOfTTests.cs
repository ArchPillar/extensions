using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Primitives.Tests;

public class OperationResultOfTTests
{
    private sealed record Order(int Id, string Customer);

    [Fact]
    public void Ok_WithValue_IsSuccessAndCarriesValue()
    {
        var order = new Order(1, "alice");

        OperationResult<Order> result = OperationResult<Order>.Ok(order);

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Same(order, result.Value);
    }

    [Fact]
    public void Created_WithValue_HasStatusCreated()
    {
        var order = new Order(2, "bob");

        OperationResult<Order> result = OperationResult<Order>.Created(order);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Same(order, result.Value);
    }

    [Fact]
    public void NotFound_Typed_HasNoValue()
    {
        OperationResult<Order> result = OperationResult<Order>.NotFound("missing");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(OperationStatus.NotFound, result.Status);
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
    public void ImplicitTaskConversion_WrapsSynchronously()
    {
        Task<OperationResult<Order>> task = OperationResult<Order>.Ok(new Order(4, "d"));

        Assert.True(task.IsCompletedSuccessfully);
        Assert.True(task.Result.IsSuccess);
    }

    [Fact]
    public void ImplicitExceptionConversion_StillWorksOnGeneric()
    {
        OperationResult<Order> source = OperationResult<Order>.Conflict("conflict");

        Exception ex = source;

        OperationException op = Assert.IsType<OperationException>(ex);
        Assert.Same(source, op.Result);
    }
}
