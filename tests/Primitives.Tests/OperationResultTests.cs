using ArchPillar.Extensions.Primitives;

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
        Assert.Empty(result.Errors);
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
    public void NotFound_WithMessage_ProducesErrorEntry()
    {
        var result = OperationResult.NotFound("missing");

        Assert.Equal(OperationStatus.NotFound, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);
        Assert.Equal("not_found", result.Errors[0].Code);
        Assert.Equal("missing", result.Errors[0].Message);
    }

    [Fact]
    public void NotFound_WithoutMessage_HasNoErrors()
    {
        var result = OperationResult.NotFound();

        Assert.Equal(OperationStatus.NotFound, result.Status);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidationFailed_CarriesErrors()
    {
        OperationError[] errors =
        [
            new("required", "Customer is required.", "CustomerId"),
            new("out_of_range", "Quantity must be between 1 and 100.", "Quantity"),
        ];

        var result = OperationResult.ValidationFailed(errors);

        Assert.Equal(OperationStatus.UnprocessableEntity, result.Status);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Failed_FromException_PreservesException()
    {
        var ex = new InvalidOperationException("boom");

        var result = OperationResult.Failed(ex);

        Assert.Same(ex, result.Exception);
        Assert.Equal(OperationStatus.InternalServerError, result.Status);
    }

    [Fact]
    public void ImplicitTaskConversion_WrapsSynchronously()
    {
        Task<OperationResult> task = OperationResult.Ok();

        Assert.True(task.IsCompletedSuccessfully);
        Assert.True(task.Result.IsSuccess);
    }

    [Fact]
    public void ImplicitExceptionConversion_ProducesOperationException()
    {
        OperationResult source = OperationResult.NotFound("missing");

        Exception ex = source;

        OperationException op = Assert.IsType<OperationException>(ex);
        Assert.Same(source, op.Result);
    }

    [Fact]
    public void Throw_OperationResult_PropagatesAsOperationException()
    {
        var result = OperationResult.Conflict("already exists");

        OperationException thrown = Assert.Throws<OperationException>(static () =>
            ThrowHelper(OperationResult.Conflict("already exists")));

        Assert.Equal(OperationStatus.Conflict, thrown.Result.Status);

        // unused locally — keeps the analyzer happy and shows the call shape.
        _ = result;

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

        OperationException ex = Assert.Throws<OperationException>(() => result.ThrowIfFailed());
        Assert.Same(result, ex.Result);
    }
}
