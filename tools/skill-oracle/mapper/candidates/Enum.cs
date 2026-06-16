// SKILL-GENERATED (archpillar-mapper). Scenario: MANY-TO-ONE enum (Low/Medium->Normal,
// High/Critical->Urgent). Judgment check: must pick EnumMapper, NOT SymmetricEnumMapper
// (which would fail the build-time bijection check).
using ArchPillar.Extensions.Mapper;
namespace MapTest.EnumScenario;

public enum PriorityX { Low, Medium, High, Critical }
public enum PriorityBucketDtoX { Normal, Urgent }

public sealed class PriorityMappers : MapperContext
{
    public EnumMapper<PriorityX, PriorityBucketDtoX> PriorityBucket { get; }
    public PriorityMappers()
    {
        PriorityBucket = CreateEnumMapper<PriorityX, PriorityBucketDtoX>(p => p switch
        {
            PriorityX.Low or PriorityX.Medium    => PriorityBucketDtoX.Normal,
            PriorityX.High or PriorityX.Critical => PriorityBucketDtoX.Urgent,
            _ => throw new ArgumentOutOfRangeException(nameof(p), p, "Unhandled priority."),
        });
        EagerBuildAll();
    }
}
