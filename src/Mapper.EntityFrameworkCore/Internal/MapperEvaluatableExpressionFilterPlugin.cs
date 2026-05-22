using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

/// <summary>
/// Prevents EF Core's funcletizer from parameterizing mapper accessors and the
/// <see cref="MapperContext"/> instances they hang off. Left as constants, those
/// accessors stay resolvable at query-compilation time so
/// <see cref="MapperInliningInterceptor"/> can read the mapper instance and inline
/// its projection expression.
/// </summary>
internal sealed class MapperEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    public bool IsEvaluatableExpression(Expression expression)
        => !IsMapperRelated(expression.Type);

    private static bool IsMapperRelated(Type type)
    {
        if (typeof(MapperContext).IsAssignableFrom(type))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(Mapper<,>)
            || definition == typeof(EnumMapper<,>)
            || definition == typeof(SymmetricEnumMapper<,>);
    }
}
