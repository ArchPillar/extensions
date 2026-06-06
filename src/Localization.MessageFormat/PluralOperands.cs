namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// The CLDR plural operands for a numeric value, as defined by Unicode Technical Standard #35. These
/// are the inputs the CLDR plural-rule conditions are written against. Build them with
/// <see cref="PluralRules.Operands(decimal, int?)"/>.
/// </summary>
/// <param name="N">The absolute value of the number.</param>
/// <param name="I">The integer digits (absolute value, truncated).</param>
/// <param name="V">The number of visible fraction digits, with trailing zeros.</param>
/// <param name="W">The number of visible fraction digits, without trailing zeros.</param>
/// <param name="F">The visible fraction digits, with trailing zeros, as an integer.</param>
/// <param name="T">The visible fraction digits, without trailing zeros, as an integer.</param>
/// <param name="E">The exponent of a compact-decimal representation (0 when not compact).</param>
/// <param name="C">The compact-decimal exponent (a synonym of <paramref name="E"/>).</param>
public readonly record struct PluralOperands(
    decimal N,
    long I,
    int V,
    int W,
    long F,
    long T,
    int E,
    int C);
