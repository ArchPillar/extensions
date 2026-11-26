using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Supports <see cref="NullHandling"/> for in-memory mapping: reads the declared
/// nullability of a source expression or destination member, and builds the call
/// that fails a <see cref="NullHandling.Strict"/> mapping on a <see langword="null"/>
/// its declarations rule out.
/// <para>
/// The failure is a call to <see cref="Fail{T}"/> rather than a <c>throw</c>
/// expression, and it is only emitted into expressions compiled for in-memory
/// mapping, never into a LINQ projection.
/// </para>
/// </summary>
internal static class NullSource
{
    private static readonly MethodInfo _failMethod =
        typeof(NullSource).GetMethod(nameof(Fail), BindingFlags.Static | BindingFlags.Public)!;

    /// <summary>Throws the <see cref="NullHandling.Strict"/> failure; called from compiled mapping delegates.</summary>
    public static T Fail<T>(string message)
    {
        throw new InvalidOperationException(message);
    }

    internal static Expression BuildFail(Type type, string message)
        => Expression.Call(_failMethod.MakeGenericMethod(type), Expression.Constant(message));

    internal static NullabilityState ReadState(Expression expression, NullabilityInfoContext context)
        => expression is MemberExpression { Member: var member }
            ? member switch
            {
                PropertyInfo property => context.Create(property).ReadState,
                FieldInfo field       => context.Create(field).ReadState,
                _                     => NullabilityState.Unknown,
            }
            : NullabilityState.Unknown;

    internal static NullabilityState WriteState(MemberInfo member, NullabilityInfoContext context)
        => member switch
        {
            PropertyInfo property => context.Create(property).WriteState,
            FieldInfo field       => context.Create(field).WriteState,
            _                     => NullabilityState.Unknown,
        };

    internal static string Describe(Expression expression)
        => expression is MemberExpression { Member: var member }
            ? $"'{member.DeclaringType?.Name}.{member.Name}'"
            : $"'{expression}'";
}
