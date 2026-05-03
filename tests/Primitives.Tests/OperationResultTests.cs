namespace ArchPillar.Extensions.Primitives.Tests;

public class OperationResultTests
{
    [Fact]
    public void Ok_DefaultStatus_IsSuccess()
    {
        var result = OperationResult.Ok();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Null(result.Problem);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(OperationStatus.Ok, true)]
    [InlineData(OperationStatus.Created, true)]
    [InlineData(OperationStatus.Accepted, true)]
    [InlineData(OperationStatus.NoContent, true)]
    [InlineData(OperationStatus.BadRequest, false)]
    [InlineData(OperationStatus.NotFound, false)]
    [InlineData(OperationStatus.UnprocessableEntity, false)]
    [InlineData(OperationStatus.InternalServerError, false)]
    [InlineData(OperationStatus.None, false)]
    public void IsSuccess_FollowsHttp2xxRange(OperationStatus status, bool expectedSuccess)
    {
        var result = new OperationResult { Status = status };

        Assert.Equal(expectedSuccess, result.IsSuccess);
        Assert.Equal(!expectedSuccess, result.IsFailure);
    }

    [Fact]
    public void NotFound_WithDetail_ProducesProblemWithDetail()
    {
        var result = OperationResult.NotFound("missing");

        Assert.Equal(OperationStatus.NotFound, result.Status);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Problem);
        Assert.Equal("not_found", result.Problem!.Type);
        Assert.Equal("Not Found", result.Problem.Title);
        Assert.Equal("missing", result.Problem.Detail);
    }

    [Fact]
    public void NotFound_WithoutDetail_ProblemDetailIsNull()
    {
        var result = OperationResult.NotFound();

        Assert.Equal(OperationStatus.NotFound, result.Status);
        Assert.NotNull(result.Problem);
        Assert.Null(result.Problem!.Detail);
    }

    [Fact]
    public void Failure_PopulatesProblem()
    {
        var result = OperationResult.Failure(
            OperationStatus.UnprocessableEntity,
            "validation",
            "Validation Failed",
            "Multiple errors occurred.");

        Assert.Equal(OperationStatus.UnprocessableEntity, result.Status);
        Assert.NotNull(result.Problem);
        Assert.Equal("validation", result.Problem!.Type);
        Assert.Equal("Validation Failed", result.Problem.Title);
        Assert.Equal("Multiple errors occurred.", result.Problem.Detail);
    }

    [Fact]
    public void Failed_FromException_PreservesExceptionAtTopLevel()
    {
        var ex = new InvalidOperationException("boom");

        var result = OperationResult.Failed(ex);

        Assert.Same(ex, result.Exception);
        Assert.Equal(OperationStatus.InternalServerError, result.Status);
        Assert.NotNull(result.Problem);
        Assert.Equal("boom", result.Problem!.Detail);
    }

    [Fact]
    public async Task ImplicitTaskConversion_WrapsSynchronouslyAsync()
    {
        Task<OperationResult> task = OperationResult.Ok();

        Assert.True(task.IsCompletedSuccessfully);
        OperationResult awaited = await task;
        Assert.True(awaited.IsSuccess);
    }

    [Fact]
    public void ImplicitExceptionConversion_ProducesOperationException()
    {
        var source = OperationResult.NotFound("missing");

        Exception ex = source;

        OperationException op = Assert.IsType<OperationException>(ex);
        Assert.Same(source, op.Result);
    }

    [Fact]
    public void Throw_OperationResult_PropagatesAsOperationException()
    {
        OperationException thrown = Assert.Throws<OperationException>(static () =>
            ThrowHelper(OperationResult.Conflict("already exists")));

        Assert.Equal(OperationStatus.Conflict, thrown.Result.Status);
        Assert.Equal("already exists", thrown.Result.Problem?.Detail);

        static void ThrowHelper(OperationResult r) => throw r;
    }

    [Fact]
    public void ThrowIfFailed_OnSuccess_DoesNotThrow()
    {
        var result = OperationResult.Ok();

        OperationResult returned = result.ThrowIfFailed();

        Assert.Same(result, returned);
    }

    [Fact]
    public void ThrowIfFailed_OnFailure_Throws()
    {
        var result = OperationResult.NotFound("missing");

        OperationException ex = Assert.Throws<OperationException>(() =>
        {
            result.ThrowIfFailed();
        });
        Assert.Same(result, ex.Result);
    }
}
