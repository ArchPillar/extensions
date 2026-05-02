using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Tests;

public class ValidationExtensionsTests
{
    [Fact]
    public void NotNull_NullValue_AddsRequiredError()
    {
        var ctx = new ValidationContext();
        ctx.NotNull<string>(null, "Field");

        Assert.Single(ctx.Errors);
        Assert.Equal("required", ctx.Errors[0].Code);
        Assert.Equal("Field", ctx.Errors[0].Field);
    }

    [Fact]
    public void NotEmpty_EmptyString_AddsRequiredError()
    {
        var ctx = new ValidationContext();
        ctx.NotEmpty(string.Empty, "Field");

        Assert.Single(ctx.Errors);
        Assert.Equal("required", ctx.Errors[0].Code);
    }

    [Fact]
    public void NotEmpty_PopulatedString_NoErrors()
    {
        var ctx = new ValidationContext();
        ctx.NotEmpty("hello", "Field");

        Assert.False(ctx.HasErrors);
    }

    [Fact]
    public void NotEmpty_EmptyCollection_AddsRequiredError()
    {
        var ctx = new ValidationContext();
        ctx.NotEmpty(Array.Empty<int>(), "Field");

        Assert.True(ctx.HasErrors);
    }

    [Fact]
    public void NotBlank_Whitespace_AddsBlankError()
    {
        var ctx = new ValidationContext();
        ctx.NotBlank("   ", "Field");

        Assert.Single(ctx.Errors);
        Assert.Equal("blank", ctx.Errors[0].Code);
    }

    [Fact]
    public void Range_OutOfRange_AddsOutOfRangeError()
    {
        var ctx = new ValidationContext();
        ctx.Range(150, 1, 100, "Quantity");

        Assert.Single(ctx.Errors);
        Assert.Equal("out_of_range", ctx.Errors[0].Code);
        OperationError error = ctx.Errors[0];
        Assert.NotNull(error.Details);
        Assert.Equal(1, error.Details!["min"]);
        Assert.Equal(100, error.Details["max"]);
    }

    [Fact]
    public void Range_InRange_NoError()
    {
        var ctx = new ValidationContext();
        ctx.Range(50, 1, 100, "Quantity");

        Assert.False(ctx.HasErrors);
    }

    [Fact]
    public void MaxLength_TooLong_AddsTooLongError()
    {
        var ctx = new ValidationContext();
        ctx.MaxLength("abcdef", 3, "Field");

        Assert.Single(ctx.Errors);
        Assert.Equal("too_long", ctx.Errors[0].Code);
    }

    [Fact]
    public void Matches_NotMatching_AddsInvalidFormatError()
    {
        var ctx = new ValidationContext();
        ctx.Matches("not-a-number", @"^\d+$", "Field");

        Assert.Single(ctx.Errors);
        Assert.Equal("invalid_format", ctx.Errors[0].Code);
    }

    [Fact]
    public void Must_FalseCondition_AddsError()
    {
        var ctx = new ValidationContext();
        ctx.Must(false, "custom_code", "custom message", "Field");

        Assert.Single(ctx.Errors);
        Assert.Equal("custom_code", ctx.Errors[0].Code);
        Assert.Equal("custom message", ctx.Errors[0].Message);
        Assert.Equal("Field", ctx.Errors[0].Field);
    }

    [Fact]
    public void Chained_AccumulatesAllErrors()
    {
        var ctx = new ValidationContext();

        ctx.NotEmpty(string.Empty, "A")
           .Range(0, 1, 100, "B")
           .Must(false, "x", "y");

        Assert.Equal(3, ctx.Errors.Count);
    }
}
