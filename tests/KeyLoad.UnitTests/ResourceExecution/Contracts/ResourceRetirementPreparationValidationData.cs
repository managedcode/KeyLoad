using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementPreparationValidationData
{
    internal const int Version = 1;
    internal const int Voters = 3;
    internal const int SourceIndex = 0;
    internal const int TargetIndex = 3;
    internal const int Zero = 0;
    internal const int One = 1;
    internal const string Invalid = "The actual native preparation fixture did not produce its required value.";
    internal const string BusinessId = "preparation-business-variation";
    internal const string BusinessJson = "{\"purpose\":\"business-variation\"}";
    internal const string PutKind = "putDocument";
    internal const string OtherResource = "preparation-other-resource";
    internal const string OtherDigest = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";
    internal static readonly Guid RetirementId = Guid.Parse("e5393178-1adb-4ea9-86d7-9de298d701db");
    internal static readonly Guid SourceAttemptId = Guid.Parse("f43bf958-d4f9-4261-9847-01aa026ecb10");
    internal static readonly Guid TargetAttemptId = Guid.Parse("759a8abb-b67a-44be-bc32-2a424a222630");
    internal static readonly Guid RemainingMoveId = Guid.Parse("c872cb4c-692e-48e3-aac9-28f0060b0cd2");
    internal static readonly Guid OtherCommandId = Guid.Parse("c1fd1ea2-1cbd-4d86-911c-473f7e053206");
    internal static readonly RegisteredPhysicalOwnerV1 Source = PhysicalOwnerDirectoryWholeFlow.Control;
    internal static readonly RegisteredPhysicalOwnerV1 Target = PhysicalOwnerDirectoryWholeFlow.Destination;
}
