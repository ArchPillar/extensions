using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Reads the declared nullability at both ends of a collection mapping: the member a
/// source collection chain starts from (<c>src.Lines</c> in
/// <c>src.Lines.Where(…).Project(mapper).ToList()</c>) and the destination member.
/// </summary>
internal static class NullableCollectionSource
{
    internal static bool IsCollection(Type type)
        => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    internal static MemberInfo? FindNullableRoot(LambdaExpression source, NullabilityInfoContext context)
        => Root(source.Body) is MemberExpression { Member: var member }
        && member switch
        {
            PropertyInfo property => context.Create(property).ReadState == NullabilityState.Nullable,
            FieldInfo field       => context.Create(field).ReadState == NullabilityState.Nullable,
            _                     => false,
        }
            ? member
            : null;

    internal static bool IsDeclaredNonNullable(MemberInfo destination, NullabilityInfoContext context)
        => destination switch
        {
            PropertyInfo property => context.Create(property).WriteState == NullabilityState.NotNull,
            FieldInfo field       => context.Create(field).WriteState == NullabilityState.NotNull,
            _                     => false,
        };

    private static Expression Root(Expression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } unary:
                    expression = unary.Operand;
                    break;

                case MethodCallExpression { Object: null, Arguments.Count: > 0 } call
                    when call.Method.DeclaringType == typeof(Enumerable)
                      || call.Method.DeclaringType == typeof(MapperExtensions):
                    expression = call.Arguments[0];
                    break;

                default:
                    return expression;
            }
        }
    }
}
