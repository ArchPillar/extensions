using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands.Tests;

public class ValidationExtensionsTests
{
    [Fact]
    public void NotNull_NullValue_AddsRequiredError()
    {
        var ctx = new ValidationContext();
        const string? Value = null;

        ctx.NotNull(Value);

        ValidationEntry entry = Assert.Single(ctx.Entries);
        Assert.Equal("Value", entry.Field);
        Assert.Equal("required", entry.Error.Type);
        Assert.Equal(OperationStatus.BadRequest, entry.Error.Status);
    }

    [Fact]
    public void NotEmpty_EmptyString_AddsRequiredError()
    {
        var ctx = new ValidationContext();
        const string Value = "";

        ctx.NotEmpty(Value);

        ValidationEntry entry = Assert.Single(ctx.Entries);
        Assert.Equal("required", entry.Error.Type);
    }

    [Fact]
    public void NotEmpty_PopulatedString_NoErrors()
    {
        var ctx = new ValidationContext();
        ctx.NotEmpty("hello");

        Assert.False(ctx.HasErrors);
    }

    [Fact]
    public void NotEmpty_EmptyCollection_AddsRequiredError()
    {
        var ctx = new ValidationContext();
        ctx.NotEmpty(Array.Empty<int>());

        Assert.True(ctx.HasErrors);
    }

    [Fact]
    public void NotBlank_Whitespace_AddsBlankError()
    {
        var ctx = new ValidationContext();
        ctx.NotBlank("   ");

        ValidationEntry entry = Assert.Single(ctx.Entries);
        Assert.Equal("blank", entry.Error.Type);
    }

    [Fact]
    public void Range_OutOfRange_AddsOutOfRangeErrorWithExtensions()
    {
        var ctx = new ValidationContext();
        const int Quantity = 150;

        ctx.Range(Quantity, 1, 100);

        ValidationEntry entry = Assert.Single(ctx.Entries);
        Assert.Equal("out_of_range", entry.Error.Type);
        Assert.NotNull(entry.Error.Extensions);
        Assert.Equal(1, entry.Error.Extensions!["min"]);
        Assert.Equal(100, entry.Error.Extensions["max"]);
        Assert.Equal(150, entry.Error.Extensions["actual"]);
    }

    [Fact]
    public void Range_InRange_NoError()
    {
        var ctx = new ValidationContext();
        ctx.Range(50, 1, 100);

        Assert.False(ctx.HasErrors);
    }

    [Fact]
    public void MaxLength_TooLong_AddsTooLongError()
    {
        var ctx = new ValidationContext();
        ctx.MaxLength("abcdef", 3);

        ValidationEntry entry = Assert.Single(ctx.Entries);
        Assert.Equal("too_long", entry.Error.Type);
    }

    [Fact]
    public void Matches_NotMatching_AddsInvalidFormatError()
    {
        var ctx = new ValidationContext();
        ctx.Matches("not-a-number", @"^\d+$");

        ValidationEntry entry = Assert.Single(ctx.Entries);
        Assert.Equal("invalid_format", entry.Error.Type);
    }

    [Fact]
    public void Authorize_FalseCondition_AddsForbiddenTopLevelError()
    {
        var ctx = new ValidationContext();
        ctx.Authorize(false);

        ValidationEntry entry = Assert.Single(ctx.Entries);
        Assert.Null(entry.Field);
        Assert.Equal("forbidden", entry.Error.Type);
        Assert.Equal(OperationStatus.Forbidden, entry.Error.Status);
    }

    [Fact]
    public void Exists_NullEntity_AddsNotFoundTopLevelError()
    {
        var ctx = new ValidationContext();
        Order? order = null;

        ctx.Exists(order);

        ValidationEntry entry = Assert.Single(ctx.Entries);
        Assert.Null(entry.Field);
        Assert.Equal("not_found", entry.Error.Type);
        Assert.Equal(OperationStatus.NotFound, entry.Error.Status);
    }

    [Fact]
    public void Must_FalseCondition_AddsBadRequest()
    {
        var ctx = new ValidationContext();
        ctx.Must(false, "custom_code", "custom message", "Field");

        ValidationEntry entry = Assert.Single(ctx.Entries);
        Assert.Equal("custom_code", entry.Error.Type);
        Assert.Equal("custom message", entry.Error.Detail);
        Assert.Equal(OperationStatus.BadRequest, entry.Error.Status);
        Assert.Equal("Field", entry.Field);
    }

    [Fact]
    public void Chained_AccumulatesAllErrors()
    {
        var ctx = new ValidationContext();
        const string A = "";
        const int B = 0;

        ctx.NotEmpty(A)
           .Range(B, 1, 100)
           .Must(false, "x", "y");

        Assert.Equal(3, ctx.Entries.Count);
    }

    private sealed class Order
    {
        public int Id { get; init; }
    }
}
