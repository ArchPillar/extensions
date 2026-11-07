using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

// Enum mapper calls stay in the query for EnumMapperQueryInterceptor to translate, and it needs the mapper
// instance. Once EF Core has lifted a closure into a query parameter that instance can no longer be read, so this
// runs before parameter extraction and leaves the mapper in the query as a constant.
internal sealed class EnumMapperAccessorResolver : ExpressionVisitor
{
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!EnumMapperQueryInterceptor.IsEnumMapperType(node.Object?.Type)
            && !EnumMapperQueryInterceptor.IsSymmetricEnumMapperType(node.Object?.Type))
        {
            return base.VisitMethodCall(node);
        }

        var mapper = MapperConstantEvaluator.Evaluate(node.Object!);
        return node.Update(Expression.Constant(mapper, node.Object!.Type), Visit(node.Arguments));
    }
}
