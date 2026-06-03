using System.Linq.Expressions;
using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies that a destination/source type mismatch in a mapping surfaces a
/// clear, actionable error (naming the mapper, the property, and both types)
/// instead of EF/LINQ's opaque "Argument types do not match" from
/// <see cref="Expression.Bind(System.Reflection.MemberInfo, Expression)"/>.
/// </summary>
public sealed class BindMismatchErrorTests
{
    private sealed class MismatchSource
    {
        public int Value { get; set; }
    }

    private sealed class MismatchDest
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Map_PropertyTypeMismatch_ThrowsClearError()
    {
        // A mapping that binds an int-typed source value to a string property.
        // C# type-checks member-init and the fluent builder, so such a mismatch
        // cannot be written directly — in practice it arises from nested-mapper
        // inlining, so the test builds the mapping via the internal constructor.
        ParameterExpression src = Expression.Parameter(typeof(MismatchSource), "src");
        LambdaExpression sourceExpr = Expression.Lambda(
            Expression.Property(src, nameof(MismatchSource.Value)), src);

        var mapping = new PropertyMapping(
            typeof(MismatchDest).GetProperty(nameof(MismatchDest.Name))!,
            sourceExpr,
            MappingKind.Required);
        var mapper = new Mapper<MismatchSource, MismatchDest>([mapping]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => mapper.Map(new MismatchSource { Value = 1 }));

        // Names the mapper, the property, and both mismatched types.
        Assert.Contains("MismatchSource", ex.Message, StringComparison.Ordinal);
        Assert.Contains("MismatchDest", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Name", ex.Message, StringComparison.Ordinal);
        Assert.Contains("String", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Int32", ex.Message, StringComparison.Ordinal);

        // Original exception preserved for diagnostics.
        Assert.IsType<ArgumentException>(ex.InnerException);
    }
}
