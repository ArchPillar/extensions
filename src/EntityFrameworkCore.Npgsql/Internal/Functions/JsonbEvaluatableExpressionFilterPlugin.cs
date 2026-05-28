using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Functions;

/// <summary>
/// Prevents EF Core's funcletizer from evaluating <c>EF.Functions.ToJsonb(...)</c> and its
/// desugared <c>JsonbBuildObjectCore(...)</c> marker client-side. Without this, an all-constant
/// shape (no query-bound values) would be treated as evaluatable and invoke the marker, which
/// throws; left as-is, the call survives to translation and is emitted as <c>jsonb_build_object(...)</c>.
/// </summary>
internal sealed class JsonbEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    public bool IsEvaluatableExpression(Expression expression)
        => expression is not MethodCallExpression { Method.DeclaringType: var declaringType, Method.Name: var name }
        || declaringType != typeof(JsonbDbFunctions)
        || (name != nameof(JsonbDbFunctions.ToJsonb) && name != nameof(JsonbDbFunctions.JsonbBuildObjectCore));
}
