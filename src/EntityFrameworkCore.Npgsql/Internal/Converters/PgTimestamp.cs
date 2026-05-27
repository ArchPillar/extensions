namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Converters;

/// <summary>
/// Helpers for converting between .NET ticks (100-ns) and PostgreSQL
/// microseconds-since-2000 used by <c>timestamp</c> / <c>timestamptz</c>.
/// </summary>
internal static class PgTimestamp
{
    public const long PostgresEpochTicks = 630_822_816_000_000_000L; // 2000-01-01T00:00:00 UTC.

    public static long DateTimeToPostgresMicros(DateTime utc)
    {
        if (utc == DateTime.MinValue)
        {
            return long.MinValue;
        }
        if (utc == DateTime.MaxValue)
        {
            return long.MaxValue;
        }

        var ticks = utc.Ticks - PostgresEpochTicks;
        return ticks / TimeSpan.TicksPerMicrosecond;
    }

    public static DateTime PostgresMicrosToUtcDateTime(long micros)
    {
        if (micros == long.MinValue)
        {
            return DateTime.MinValue;
        }
        if (micros == long.MaxValue)
        {
            return DateTime.MaxValue;
        }

        var ticks = (micros * TimeSpan.TicksPerMicrosecond) + PostgresEpochTicks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    public static DateTimeOffset PostgresMicrosToUtcDateTimeOffset(long micros)
    {
        if (micros == long.MinValue)
        {
            return DateTimeOffset.MinValue;
        }
        if (micros == long.MaxValue)
        {
            return DateTimeOffset.MaxValue;
        }

        return new DateTimeOffset(PostgresMicrosToUtcDateTime(micros), TimeSpan.Zero);
    }

    public static long DateTimeOffsetToPostgresMicros(DateTimeOffset value)
    {
        if (value == DateTimeOffset.MinValue)
        {
            return long.MinValue;
        }
        if (value == DateTimeOffset.MaxValue)
        {
            return long.MaxValue;
        }

        return DateTimeToPostgresMicros(value.UtcDateTime);
    }
}
