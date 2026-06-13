namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

/// <summary>
/// Sample priority enum used by integration tests to verify CLR enum ↔ int4
/// wire conversion.
/// </summary>
public enum TestPriority
{
    Low = 1,
    Normal = 5,
    High = 9,
    Critical = 99,
}

/// <summary>
/// Test row covering every Tier 1 wire and SQL feature: <c>uuid</c>,
/// <c>timestamptz</c> for both <see cref="DateTime"/> and <see cref="DateTimeOffset"/>,
/// and an integer-backed CLR enum.
/// </summary>
public sealed class TestRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public TestPriority Priority { get; set; }
}
