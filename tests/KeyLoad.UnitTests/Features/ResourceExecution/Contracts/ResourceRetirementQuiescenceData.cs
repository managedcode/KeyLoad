using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceData
{
    internal const int Version = 1;
    internal const int Zero = 0;
    internal const int One = 1;
    internal const int TwoCold = 2;
    internal const string Root = "root";
    internal const string NewPartitionKey = "retirement-quiescence-new";
    internal const string NewResource = "retirement-quiescence-business";
    internal const string NewDocument = "quiescence-business";
    internal const string Json = "{\"text\":\"quiescence remains writable\"}";
    internal const string Retiring = "The resource lifetime is retiring.";
    internal const string Invalid = "The genuine quiescence fixture did not produce its expected result.";
    internal const string OriginalsKey = "KeyLoad.ResourceRetirement.QuiescenceOriginals";
    internal const string SourceOwnerKey = "KeyLoad.ResourceRetirement.QuiescenceSourceOwner";
    internal const string TargetOwnerKey = "KeyLoad.ResourceRetirement.QuiescenceTargetOwner";
    internal const string ExpectedFailuresKey = "KeyLoad.ResourceRetirement.QuiescenceExpectedFailures";
    internal static readonly Guid RetirementId = Guid.Parse("9fbe3fd0-e64f-4fe5-b265-8d1e545652ed");
    internal static readonly Guid NewMoveId = Guid.Parse("d9583664-794d-436d-ab35-c380007a2162");
    internal static readonly Guid NewMoveCommandId = Guid.Parse("4d49cd82-c58d-4b19-8666-38205d373c98");
    internal static readonly PartitionRef Partition = ControlledPartitionMovementCorpus.Partition;
    internal static readonly PartitionRef OtherPartition = Partition with { PartitionKey = NewPartitionKey };
}
