using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.Tests;

// ---------------------------------------------------------------------------
// Test models — Money value object with implicit conversion
// ---------------------------------------------------------------------------

public record Money(decimal Amount, string Currency)
{
    public static implicit operator decimal(Money money) => money.Amount;
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
