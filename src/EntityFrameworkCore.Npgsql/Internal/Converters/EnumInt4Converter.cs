using System.Collections.Frozen;
using Npgsql.Internal;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Converters;

/// <summary>
/// Wire converter that maps a CLR <typeparamref name="TEnum"/> ↔ PostgreSQL <c>int4</c>
/// using prebuilt dictionaries. Unknown integers from the database fall back to
/// <c>default(TEnum)</c>.
/// </summary>
internal sealed class EnumInt4Converter<TEnum> : PgBufferedConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly FrozenDictionary<int, TEnum> _intToEnum = BuildIntToEnum();
    private static readonly FrozenDictionary<TEnum, int> _enumToInt = BuildEnumToInt();

    protected override TEnum ReadCore(PgReader reader)
    {
        var raw = reader.ReadInt32();
        return _intToEnum.TryGetValue(raw, out TEnum value) ? value : default;
    }

    protected override void WriteCore(PgWriter writer, TEnum value)
    {
        var raw = _enumToInt.TryGetValue(value, out var v)
            ? v
            : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteInt32(raw);
    }

    public override Size GetSize(SizeContext context, TEnum value, ref object? writeState)
        => sizeof(int);

    public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
    {
        if (format == DataFormat.Binary)
        {
            bufferRequirements = BufferRequirements.CreateFixedSize(sizeof(int));
            return true;
        }

        bufferRequirements = default;
        return false;
    }

    private static FrozenDictionary<int, TEnum> BuildIntToEnum()
    {
        var values = (TEnum[])Enum.GetValues(typeof(TEnum));
        var dict = new Dictionary<int, TEnum>(values.Length);
        foreach (TEnum v in values)
        {
            var i = Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
            dict[i] = v;
        }

        return dict.ToFrozenDictionary();
    }

    private static FrozenDictionary<TEnum, int> BuildEnumToInt()
    {
        var values = (TEnum[])Enum.GetValues(typeof(TEnum));
        var dict = new Dictionary<TEnum, int>(values.Length);
        foreach (TEnum v in values)
        {
            dict[v] = Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        return dict.ToFrozenDictionary();
    }
}
