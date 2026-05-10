using ArchPillar.Extensions.Identifiers.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArchPillar.Extensions.Identifiers.EntityFrameworkCore;

/// <summary>
/// Extension methods on <see cref="PropertyBuilder{TProperty}"/> for
/// per-property <c>Id&lt;T&gt;</c> conversion configuration.
/// </summary>
public static class PropertyBuilderExtensions
{
    /// <summary>
    /// Explicitly registers the <c>Id&lt;T&gt; ↔ Guid</c> value converter and
    /// comparer on this property. Use this when the auto-convention from
    /// <c>UseArchPillarTypedIds()</c> is not active or when you need surgical
    /// per-property control.
    /// </summary>
    public static PropertyBuilder<TId> HasIdConversion<TId>(
        this PropertyBuilder<TId> builder)
        where TId : struct, IId
    {
        ApplyIdConversion(builder, typeof(TId));
        return builder;
    }

    /// <summary>
    /// Nullable overload of <see cref="HasIdConversion{TId}(PropertyBuilder{TId})"/>
    /// for properties whose CLR type is <c>Id&lt;T&gt;?</c>.
    /// </summary>
    public static PropertyBuilder<TId?> HasIdConversion<TId>(
        this PropertyBuilder<TId?> builder)
        where TId : struct, IId
    {
        ApplyIdConversion(builder, typeof(TId));
        return builder;
    }

    private static void ApplyIdConversion(PropertyBuilder builder, Type idType)
    {
        Type typeArg = idType.GetGenericArguments()[0];

        var converter = (ValueConverter)Activator.CreateInstance(
            typeof(IdValueConverter<>).MakeGenericType(typeArg))!;
        var comparer = (ValueComparer)Activator.CreateInstance(
            typeof(IdValueComparer<>).MakeGenericType(typeArg))!;

        builder.HasConversion(converter, comparer);
    }
}
