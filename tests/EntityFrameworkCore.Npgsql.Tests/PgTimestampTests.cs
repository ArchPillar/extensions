using ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Converters;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

public sealed class PgTimestampTests
{
    [Fact]
    public void DateTimeMinValue_MapsTo_LongMin()
    {
        Assert.Equal(long.MinValue, PgTimestamp.DateTimeToPostgresMicros(DateTime.MinValue));
    }

    [Fact]
    public void DateTimeMaxValue_MapsTo_LongMax()
    {
        Assert.Equal(long.MaxValue, PgTimestamp.DateTimeToPostgresMicros(DateTime.MaxValue));
    }

    [Fact]
    public void Epoch_MapsTo_Zero()
    {
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(0L, PgTimestamp.DateTimeToPostgresMicros(epoch));
    }

    [Fact]
    public void RoundTrip_PreservesMicrosecondPrecision()
    {
        DateTime original = new DateTime(2024, 6, 15, 12, 34, 56, DateTimeKind.Utc).AddTicks(7890);
        var micros = PgTimestamp.DateTimeToPostgresMicros(original);
        DateTime back = PgTimestamp.PostgresMicrosToUtcDateTime(micros);

        // Postgres timestamptz precision is microseconds, so sub-microsecond ticks are lost.
        var expected = new DateTime(original.Ticks - (original.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);
        Assert.Equal(expected, back);
    }

    [Fact]
    public void Infinity_RoundTrip_PreservesMinAndMax()
    {
        Assert.Equal(DateTime.MinValue, PgTimestamp.PostgresMicrosToUtcDateTime(long.MinValue));
        Assert.Equal(DateTime.MaxValue, PgTimestamp.PostgresMicrosToUtcDateTime(long.MaxValue));
        Assert.Equal(DateTimeOffset.MinValue, PgTimestamp.PostgresMicrosToUtcDateTimeOffset(long.MinValue));
        Assert.Equal(DateTimeOffset.MaxValue, PgTimestamp.PostgresMicrosToUtcDateTimeOffset(long.MaxValue));
    }

    [Fact]
    public void DateTimeOffset_DifferentOffsets_SameInstant_MapToSameMicros()
    {
        var utc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var plusTwo = new DateTimeOffset(2024, 1, 1, 14, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(
            PgTimestamp.DateTimeOffsetToPostgresMicros(utc),
            PgTimestamp.DateTimeOffsetToPostgresMicros(plusTwo));
    }
}
