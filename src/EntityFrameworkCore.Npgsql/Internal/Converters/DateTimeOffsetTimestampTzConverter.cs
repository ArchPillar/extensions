using Npgsql.Internal;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Converters;

/// <summary>
/// Wire converter for <see cref="DateTimeOffset"/> ↔ PostgreSQL <c>timestamptz</c>.
/// Stores the UTC instant regardless of the input offset.
/// <see cref="DateTimeOffset.MinValue"/> / <see cref="DateTimeOffset.MaxValue"/>
/// map to PostgreSQL <c>-infinity</c> / <c>infinity</c>.
/// </summary>
internal sealed class DateTimeOffsetTimestampTzConverter : PgBufferedConverter<DateTimeOffset>
{
    protected override DateTimeOffset ReadCore(PgReader reader)
    {
        var micros = reader.ReadInt64();
        return PgTimestamp.PostgresMicrosToUtcDateTimeOffset(micros);
    }

    protected override void WriteCore(PgWriter writer, DateTimeOffset value)
    {
        var micros = PgTimestamp.DateTimeOffsetToPostgresMicros(value);
        writer.WriteInt64(micros);
    }

    public override Size GetSize(SizeContext context, DateTimeOffset value, ref object? writeState)
        => sizeof(long);

    public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
    {
        if (format == DataFormat.Binary)
        {
            bufferRequirements = BufferRequirements.CreateFixedSize(sizeof(long));
            return true;
        }

        bufferRequirements = default;
        return false;
    }
}
