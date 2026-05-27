using Npgsql.Internal;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Converters;

/// <summary>
/// Wire converter for <see cref="DateTime"/> ↔ PostgreSQL <c>timestamptz</c>.
/// Always stores UTC and rejects <see cref="DateTimeKind.Unspecified"/>.
/// <see cref="DateTime.MinValue"/> / <see cref="DateTime.MaxValue"/> map to
/// PostgreSQL <c>-infinity</c> / <c>infinity</c>.
/// </summary>
internal sealed class DateTimeTimestampTzConverter : PgBufferedConverter<DateTime>
{
    protected override DateTime ReadCore(PgReader reader)
    {
        var micros = reader.ReadInt64();
        return PgTimestamp.PostgresMicrosToUtcDateTime(micros);
    }

    protected override void WriteCore(PgWriter writer, DateTime value)
    {
        DateTime utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => throw new ArgumentException(
                "Cannot write DateTime with Kind=Unspecified to a 'timestamp with time zone' column. " +
                "Use a UTC DateTime, a Local DateTime, or DateTimeOffset.",
                nameof(value)),
        };

        var micros = PgTimestamp.DateTimeToPostgresMicros(utc);
        writer.WriteInt64(micros);
    }

    public override Size GetSize(SizeContext context, DateTime value, ref object? writeState)
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
