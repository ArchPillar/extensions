namespace ArchPillar.Extensions.Primitives.Tests;

public class OperationExceptionTests
{
    [Fact]
    public void Constructor_FromResult_SetsResult()
    {
        var result = OperationResult.NotFound("missing");

        var ex = new OperationException(result);

        Assert.Same(result, ex.Result);
        Assert.Contains("NotFound", ex.Message);
    }

    [Fact]
    public void Constructor_FromStatusAndMessage_BuildsResult()
    {
        var ex = new OperationException(OperationStatus.Conflict, "already exists");

        Assert.Equal(OperationStatus.Conflict, ex.Result.Status);
        Assert.Single(ex.Result.Errors);
        Assert.Equal("already exists", ex.Result.Errors[0].Message);
    }

    [Fact]
    public void Constructor_FromResult_PreservesInnerException()
    {
        var inner = new InvalidOperationException("boom");
        var result = OperationResult.Failed(inner);

        var ex = new OperationException(result);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void Constructor_NullResult_Throws()
        => Assert.Throws<ArgumentNullException>(static () => new OperationException(null!));
}
