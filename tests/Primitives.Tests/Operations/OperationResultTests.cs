namespace ArchPillar.Extensions.Operations.Tests;

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
    public void NotFound_RequiredDetail_PopulatesProblem()
    {
        OperationFailure result = OperationResult.NotFound("missing");

        Assert.Equal(OperationStatus.NotFound, result.Status);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Problem);
        Assert.Equal("not_found", result.Problem!.Type);
        Assert.Equal("Not Found", result.Problem.Title);
        Assert.Equal("missing", result.Problem.Detail);
    }

    [Fact]
    public void NotFound_TypeOverride_ReplacesDefault()
    {
        OperationFailure result = OperationResult.NotFound("missing", type: "order_missing");

        Assert.Equal("order_missing", result.Problem!.Type);
    }

    [Fact]
    public void BadRequest_WithErrorsDictionary_PopulatesProblemErrors()
    {
        var errors = new Dictionary<string, IReadOnlyList<OperationError>>
        {
            ["Quantity"] = [new OperationError("out_of_range", "must be 1..100", OperationStatus.BadRequest)],
        };

        OperationFailure result = OperationResult.BadRequest("Validation failed.", errors: errors);

        Assert.NotNull(result.Problem!.Errors);
        Assert.True(result.Problem.Errors!.ContainsKey("Quantity"));
    }

    [Fact]
    public void Conflict_WithExtensions_PopulatesExtensions()
    {
        var ext = new Dictionary<string, object?> { ["lockedBy"] = "alice" };

        OperationFailure result = OperationResult.Conflict("Order is locked.", extensions: ext);

        Assert.NotNull(result.Problem!.Extensions);
        Assert.Equal("alice", result.Problem.Extensions!["lockedBy"]);
    }

    [Fact]
    public void Failure_PopulatesEverything()
    {
        OperationFailure result = OperationResult.Failure(
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

        OperationFailure result = OperationResult.Failed(ex);

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
        OperationFailure source = OperationResult.NotFound("missing");

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
        OperationFailure result = OperationResult.NotFound("missing");

        OperationException ex = Assert.Throws<OperationException>(() =>
        {
            result.ThrowIfFailed();
        });
        Assert.Same(result, ex.Result);
    }
}
