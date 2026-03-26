using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Tests;

// ---------------------------------------------------------------------------
// Test models — Money value object with implicit conversion
// ---------------------------------------------------------------------------

public interface IAmount
{
    decimal Amount { get; }
}

public record Money(decimal Amount, string Currency) : IAmount
{
    public static implicit operator decimal(Money money) => money.Amount;

    public bool IsPositive() => Amount > 0;
}

public record Fee(decimal Amount, string Description) : IAmount
{
    public static implicit operator decimal(Fee fee) => fee.Amount;
}

public class Invoice
{
    public required int Id { get; set; }
    public required Money Total { get; set; }
    public required Money Tax { get; set; }
}

public class InvoiceDto
{
    public int Id { get; set; }
    public decimal Total { get; set; }
    public decimal Tax { get; set; }
}

public class Payment
{
    public required int Id { get; set; }
    public required Money Amount { get; set; }
}

public class PaymentDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public bool IsPositive { get; set; }
}

public class MixedAmountSource
{
    public required int Id { get; set; }
    public required Money Total { get; set; }
    public required Fee ServiceFee { get; set; }
}

public class MixedAmountDto
{
    public int Id { get; set; }
    public decimal Total { get; set; }
    public decimal ServiceFee { get; set; }
}

public class Wrapper<T>
{
    public required T Value { get; set; }

    public T Unwrap() => Value;
}

public class WrappedDecimal : Wrapper<decimal>;

public class UnwrapSource
{
    public required int Id { get; set; }
    public required WrappedDecimal Price { get; set; }
}

public class UnwrapDest
{
    public int Id { get; set; }
    public decimal Price { get; set; }
}

// ---------------------------------------------------------------------------
// Test transformers
// ---------------------------------------------------------------------------

/// <summary>
/// Replaces <c>(decimal)money</c> cast expressions with <c>money.Amount</c>
/// member access — the canonical use case for expression transformers.
/// </summary>
public sealed class MoneyToAmountTransformer : ExpressionVisitor, IExpressionTransformer
{
    public Expression Transform(Expression expression)
    {
        return Visit(expression);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Convert
            && node.Type == typeof(decimal)
            && node.Operand.Type == typeof(Money))
        {
            return Expression.Property(Visit(node.Operand)!, typeof(Money), nameof(Money.Amount));
        }

        return base.VisitUnary(node);
    }
}

/// <summary>
/// Appends a suffix to all string constant expressions. Used to verify
/// transformer ordering (global → context → mapper).
/// </summary>
public sealed class StringSuffixTransformer(string suffix) : ExpressionVisitor, IExpressionTransformer
{
    public Expression Transform(Expression expression)
    {
        return Visit(expression);
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Type == typeof(string) && node.Value is string value)
        {
            return Expression.Constant(value + suffix);
        }

        return base.VisitConstant(node);
    }
}

// ---------------------------------------------------------------------------
// Test transformers using abstract base classes
// ---------------------------------------------------------------------------

/// <summary>
/// Replaces <c>(decimal)amount</c> with <c>amount.Amount</c> for any
/// <see cref="IAmount"/> implementor, demonstrating interface-based matching.
/// </summary>
public sealed class AmountInterfaceCastTransformer : CastTransformer<IAmount, decimal>
{
    protected override Expression Replacement(Expression operand)
    {
        return Expression.Property(operand, nameof(IAmount.Amount));
    }
}

/// <summary>
/// Replaces <c>(decimal)money</c> with <c>money.Amount</c> using the
/// <see cref="CastTransformer{TSource,TTarget}"/> base class.
/// </summary>
public sealed class MoneyAmountCastTransformer : CastTransformer<Money, decimal>
{
    protected override Expression Replacement(Expression operand)
    {
        return Expression.Property(operand, nameof(Money.Amount));
    }
}

/// <summary>
/// Replaces <c>wrapper.Unwrap()</c> with <c>wrapper.Value</c> using the
/// <see cref="MethodCallTransformer"/> base class, targeting a method
/// defined on a generic base class (<see cref="Wrapper{T}"/>).
/// </summary>
public sealed class UnwrapTransformer : MethodCallTransformer
{
    protected override MethodInfo Method { get; } =
        typeof(Wrapper<>).GetMethod(nameof(Wrapper<object>.Unwrap))!;

    protected override Expression Replacement(
        Expression? instance, IReadOnlyList<Expression> arguments)
    {
        return Expression.Property(instance!, nameof(Wrapper<object>.Value));
    }
}

/// <summary>
/// Replaces <c>money.IsPositive()</c> with <c>money.Amount &gt; 0</c> using
/// the <see cref="MethodCallTransformer"/> base class.
/// </summary>
public sealed class MoneyIsPositiveTransformer : MethodCallTransformer
{
    protected override MethodInfo Method { get; } =
        typeof(Money).GetMethod(nameof(Money.IsPositive))!;

    protected override Expression Replacement(
        Expression? instance, IReadOnlyList<Expression> arguments)
    {
        return Expression.GreaterThan(
            Expression.Property(instance!, nameof(Money.Amount)),
            Expression.Constant(0m));
    }
}

// ---------------------------------------------------------------------------
// Test mapper contexts
// ---------------------------------------------------------------------------

public class MoneyMappers : MapperContext
{
    public Mapper<Invoice, InvoiceDto> Invoice { get; }

    public MoneyMappers()
    {
        AddTransformer(new MoneyToAmountTransformer());

        Invoice = CreateMapper<Invoice, InvoiceDto>(src => new InvoiceDto
        {
            Id    = src.Id,
            Total = (decimal)src.Total,
            Tax   = (decimal)src.Tax,
        });
    }
}

public class GlobalTransformerMappers : MapperContext
{
    public Mapper<Invoice, InvoiceDto> Invoice { get; }

    public GlobalTransformerMappers(GlobalMapperOptions globalOptions)
        : base(globalOptions)
    {
        Invoice = CreateMapper<Invoice, InvoiceDto>(src => new InvoiceDto
        {
            Id    = src.Id,
            Total = (decimal)src.Total,
            Tax   = (decimal)src.Tax,
        });
    }
}

// ---------------------------------------------------------------------------
// Source/dest for ordering test
// ---------------------------------------------------------------------------

public class LabelSource
{
    public required string Tag { get; set; }
}

public class LabelDest
{
    public string Tag { get; set; } = "";
}

public class TransformerOrderMappers : MapperContext
{
    public Mapper<LabelSource, LabelDest> Label { get; }

    public TransformerOrderMappers(GlobalMapperOptions globalOptions)
        : base(globalOptions)
    {
        AddTransformer(new StringSuffixTransformer("-ctx"));

        Label = CreateMapper<LabelSource, LabelDest>(_ => new LabelDest
        {
            Tag = "base",
        })
        .WithTransformers(new StringSuffixTransformer("-mapper"));
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class ExpressionTransformerTests
{
    // -----------------------------------------------------------------------
    // Per-context transformer
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_PerContextTransformer_TransformsMoneyToAmount()
    {
        var mappers = new MoneyMappers();

        var invoice = new Invoice
        {
            Id    = 1,
            Total = new Money(99.95m, "USD"),
            Tax   = new Money(12.50m, "USD"),
        };

        InvoiceDto dto = mappers.Invoice.Map(invoice)!;

        Assert.Equal(1, dto.Id);
        Assert.Equal(99.95m, dto.Total);
        Assert.Equal(12.50m, dto.Tax);
    }

    [Fact]
    public void ToExpression_PerContextTransformer_ProducesTranslatableExpression()
    {
        var mappers = new MoneyMappers();

        var expr = mappers.Invoice.ToExpression();

        var invoice = new Invoice
        {
            Id    = 2,
            Total = new Money(200m, "EUR"),
            Tax   = new Money(25m, "EUR"),
        };

        InvoiceDto dto = expr.Compile()(invoice);

        Assert.Equal(2, dto.Id);
        Assert.Equal(200m, dto.Total);
        Assert.Equal(25m, dto.Tax);
    }

    // -----------------------------------------------------------------------
    // Global transformer
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_GlobalTransformer_TransformsMoneyToAmount()
    {
        GlobalMapperOptions globalOptions = new GlobalMapperOptions()
            .AddTransformer(new MoneyToAmountTransformer());

        var mappers = new GlobalTransformerMappers(globalOptions);

        var invoice = new Invoice
        {
            Id    = 3,
            Total = new Money(50m, "GBP"),
            Tax   = new Money(10m, "GBP"),
        };

        InvoiceDto dto = mappers.Invoice.Map(invoice)!;

        Assert.Equal(3, dto.Id);
        Assert.Equal(50m, dto.Total);
        Assert.Equal(10m, dto.Tax);
    }

    // -----------------------------------------------------------------------
    // Per-mapper transformer
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_PerMapperTransformer_AppliedViaWithTransformers()
    {
        var mappers = new PerMapperTransformerMappers();

        var invoice = new Invoice
        {
            Id    = 4,
            Total = new Money(75m, "SEK"),
            Tax   = new Money(15m, "SEK"),
        };

        InvoiceDto dto = mappers.Invoice.Map(invoice)!;

        Assert.Equal(4, dto.Id);
        Assert.Equal(75m, dto.Total);
        Assert.Equal(15m, dto.Tax);
    }

    // -----------------------------------------------------------------------
    // Ordering: global → context → mapper
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_TransformerOrdering_GlobalThenContextThenMapper()
    {
        GlobalMapperOptions globalOptions = new GlobalMapperOptions()
            .AddTransformer(new StringSuffixTransformer("-global"));

        var mappers = new TransformerOrderMappers(globalOptions);

        var source = new LabelSource { Tag = "ignored" };
        LabelDest dest = mappers.Label.Map(source)!;

        // The constant "base" is visited by global first, then context, then mapper.
        // Each StringSuffixTransformer appends its suffix to string constants.
        Assert.Equal("base-global-ctx-mapper", dest.Tag);
    }

    // -----------------------------------------------------------------------
    // CastTransformer base class
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_CastTransformerBase_ReplacesImplicitConversion()
    {
        var mappers = new CastTransformerBaseMappers();

        var invoice = new Invoice
        {
            Id    = 5,
            Total = new Money(42m, "NOK"),
            Tax   = new Money(8m, "NOK"),
        };

        InvoiceDto dto = mappers.Invoice.Map(invoice)!;

        Assert.Equal(5, dto.Id);
        Assert.Equal(42m, dto.Total);
        Assert.Equal(8m, dto.Tax);
    }

    [Fact]
    public void Map_CastTransformerBase_MatchesViaInterface()
    {
        var mappers = new InterfaceCastTransformerMappers();

        var source = new MixedAmountSource
        {
            Id         = 1,
            Total      = new Money(100m, "USD"),
            ServiceFee = new Fee(5m, "Processing"),
        };

        MixedAmountDto dto = mappers.Mixed.Map(source)!;

        Assert.Equal(1, dto.Id);
        Assert.Equal(100m, dto.Total);
        Assert.Equal(5m, dto.ServiceFee);
    }

    // -----------------------------------------------------------------------
    // MethodCallTransformer base class
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_MethodCallTransformerBase_ReplacesMethodCall()
    {
        var mappers = new MethodCallTransformerBaseMappers();

        var payment = new Payment
        {
            Id     = 1,
            Amount = new Money(50m, "USD"),
        };

        PaymentDto dto = mappers.Payment.Map(payment)!;

        Assert.Equal(1, dto.Id);
        Assert.Equal(50m, dto.Amount);
        Assert.True(dto.IsPositive);
    }

    [Fact]
    public void Map_MethodCallTransformerBase_NegativeAmount()
    {
        var mappers = new MethodCallTransformerBaseMappers();

        var payment = new Payment
        {
            Id     = 2,
            Amount = new Money(-10m, "USD"),
        };

        PaymentDto dto = mappers.Payment.Map(payment)!;

        Assert.Equal(-10m, dto.Amount);
        Assert.False(dto.IsPositive);
    }

    [Fact]
    public void Map_MethodCallTransformerBase_MatchesGenericBaseClassMethod()
    {
        var mappers = new GenericBaseMethodMappers();

        var source = new UnwrapSource
        {
            Id    = 1,
            Price = new WrappedDecimal { Value = 42.5m },
        };

        UnwrapDest dto = mappers.Unwrap.Map(source)!;

        Assert.Equal(1, dto.Id);
        Assert.Equal(42.5m, dto.Price);
    }

    // -----------------------------------------------------------------------
    // No transformers — existing behavior unaffected
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NoTransformers_WorksAsUsual()
    {
        var mappers = new TestMappers();

        var order = new Order
        {
            Id       = 10,
            Status   = OrderStatus.Pending,
            Customer = new Customer { Name = "Test", Email = "test@test.com" },
            Lines    = [],
        };

        OrderDto dto = mappers.Order.Map(order)!;

        Assert.Equal(10, dto.Id);
    }

    // -----------------------------------------------------------------------
    // Transformer returns wrong type — clear error
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_TransformerReturnsNonLambda_ThrowsWithTransformerName()
    {
        var mappers = new BodyOnlyTransformerMappers();

        var invoice = new Invoice
        {
            Id    = 1,
            Total = new Money(10m, "USD"),
            Tax   = new Money(1m, "USD"),
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => mappers.Invoice.Map(invoice));

        Assert.Contains("BodyOnlyTransformer", ex.Message);
        Assert.Contains("Expression<Func<Invoice, InvoiceDto>>", ex.Message);
    }

    [Fact]
    public void Map_TransformerReturnsNull_ThrowsWithTransformerName()
    {
        var mappers = new NullTransformerMappers();

        var invoice = new Invoice
        {
            Id    = 1,
            Total = new Money(10m, "USD"),
            Tax   = new Money(1m, "USD"),
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => mappers.Invoice.Map(invoice));

        Assert.Contains("NullTransformer", ex.Message);
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void Map_TransformerDestroysBody_ThrowsWithMemberInitMessage()
    {
        var mappers = new MethodCallBodyTransformerMappers();

        var invoice = new Invoice
        {
            Id    = 1,
            Total = new Money(10m, "USD"),
            Tax   = new Money(1m, "USD"),
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => mappers.Invoice.Map(invoice));

        Assert.Contains("MethodCallBodyTransformer", ex.Message);
        Assert.Contains("MemberInit", ex.Message);
    }
}

// ---------------------------------------------------------------------------
// Per-mapper transformer context (declared after test class to keep tests
// readable — file-scoped namespace allows this)
// ---------------------------------------------------------------------------

public class PerMapperTransformerMappers : MapperContext
{
    public Mapper<Invoice, InvoiceDto> Invoice { get; }

    public PerMapperTransformerMappers()
    {
        Invoice = CreateMapper<Invoice, InvoiceDto>(src => new InvoiceDto
        {
            Id    = src.Id,
            Total = (decimal)src.Total,
            Tax   = (decimal)src.Tax,
        })
        .WithTransformers(new MoneyToAmountTransformer());
    }
}

// ---------------------------------------------------------------------------
// Mapper contexts for base class tests
// ---------------------------------------------------------------------------

public class CastTransformerBaseMappers : MapperContext
{
    public Mapper<Invoice, InvoiceDto> Invoice { get; }

    public CastTransformerBaseMappers()
    {
        AddTransformer(new MoneyAmountCastTransformer());

        Invoice = CreateMapper<Invoice, InvoiceDto>(src => new InvoiceDto
        {
            Id    = src.Id,
            Total = (decimal)src.Total,
            Tax   = (decimal)src.Tax,
        });
    }
}

public class InterfaceCastTransformerMappers : MapperContext
{
    public Mapper<MixedAmountSource, MixedAmountDto> Mixed { get; }

    public InterfaceCastTransformerMappers()
    {
        AddTransformer(new AmountInterfaceCastTransformer());

        Mixed = CreateMapper<MixedAmountSource, MixedAmountDto>(src => new MixedAmountDto
        {
            Id         = src.Id,
            Total      = (decimal)src.Total,
            ServiceFee = (decimal)src.ServiceFee,
        });
    }
}

public class MethodCallTransformerBaseMappers : MapperContext
{
    public Mapper<Payment, PaymentDto> Payment { get; }

    public MethodCallTransformerBaseMappers()
    {
        AddTransformer(new MoneyAmountCastTransformer());
        AddTransformer(new MoneyIsPositiveTransformer());

        Payment = CreateMapper<Payment, PaymentDto>(src => new PaymentDto
        {
            Id         = src.Id,
            Amount     = (decimal)src.Amount,
            IsPositive = src.Amount.IsPositive(),
        });
    }
}

public class GenericBaseMethodMappers : MapperContext
{
    public Mapper<UnwrapSource, UnwrapDest> Unwrap { get; }

    public GenericBaseMethodMappers()
    {
        AddTransformer(new UnwrapTransformer());

        Unwrap = CreateMapper<UnwrapSource, UnwrapDest>(src => new UnwrapDest
        {
            Id    = src.Id,
            Price = src.Price.Unwrap(),
        });
    }
}

// ---------------------------------------------------------------------------
// Faulty transformers — for validation tests
// ---------------------------------------------------------------------------

/// <summary>
/// Returns just the lambda body (a <see cref="MemberInitExpression"/>)
/// instead of the full lambda — simulates a common implementation mistake.
/// </summary>
public sealed class BodyOnlyTransformer : IExpressionTransformer
{
    public Expression Transform(Expression expression)
    {
        return ((LambdaExpression)expression).Body;
    }
}

/// <summary>
/// Returns <c>null</c> — simulates a broken transformer.
/// </summary>
public sealed class NullTransformer : IExpressionTransformer
{
    public Expression Transform(Expression expression)
    {
        return null!;
    }
}

/// <summary>
/// Replaces the <see cref="MemberInitExpression"/> body with a method call,
/// destroying the required structure — simulates a transformer that wraps
/// or replaces the body incorrectly.
/// </summary>
public sealed class MethodCallBodyTransformer : IExpressionTransformer
{
    public Expression Transform(Expression expression)
    {
        var lambda = (LambdaExpression)expression;
        Expression body = Expression.Call(
            typeof(Activator), nameof(Activator.CreateInstance), [lambda.Body.Type]);
        return Expression.Lambda(body, lambda.Parameters);
    }
}

public class BodyOnlyTransformerMappers : MapperContext
{
    public Mapper<Invoice, InvoiceDto> Invoice { get; }

    public BodyOnlyTransformerMappers()
    {
        AddTransformer(new BodyOnlyTransformer());

        Invoice = CreateMapper<Invoice, InvoiceDto>(src => new InvoiceDto
        {
            Id    = src.Id,
            Total = (decimal)src.Total,
            Tax   = (decimal)src.Tax,
        });
    }
}

public class NullTransformerMappers : MapperContext
{
    public Mapper<Invoice, InvoiceDto> Invoice { get; }

    public NullTransformerMappers()
    {
        AddTransformer(new NullTransformer());

        Invoice = CreateMapper<Invoice, InvoiceDto>(src => new InvoiceDto
        {
            Id    = src.Id,
            Total = (decimal)src.Total,
            Tax   = (decimal)src.Tax,
        });
    }
}

public class MethodCallBodyTransformerMappers : MapperContext
{
    public Mapper<Invoice, InvoiceDto> Invoice { get; }

    public MethodCallBodyTransformerMappers()
    {
        AddTransformer(new MethodCallBodyTransformer());

        Invoice = CreateMapper<Invoice, InvoiceDto>(src => new InvoiceDto
        {
            Id    = src.Id,
            Total = (decimal)src.Total,
            Tax   = (decimal)src.Tax,
        });
    }
}
