namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsProbeFixtureProtocol
{
    internal const int Version = 2;
    internal const int MaximumRecordBytes = 8_192;
    internal const int MaximumVoterBytes = 1_048_576;
    internal const int MaximumFilesPerVoter = 400;
    internal const int MaximumArms = 32;
    internal const int MaximumActiveGatesPerVoter = 4;
    internal const int MaximumMarkersPerRequestPerVoter = 8;
    internal const int MaximumMarkerRecordsPerArm = MaximumMarkersPerRequestPerVoter * RequestCqrsRf3Protocol.NodeCount;
    internal const int InitialRecordCapacity = 512;
    internal const string RootPrefix = "keyload-c1-probe-";
    internal const string OwnerFileName = "owner.json";
    internal const string OwnerKind = "Owner";
    internal const string ArmKind = "Arm";
    internal const string ReleaseKind = "Release";
    internal const string MarkerKind = "Marker";
    internal const string VersionField = "Version";
    internal const string KindField = "Kind";
    internal const string SessionField = "SessionId";
    internal const string VoterField = "Voter";
    internal const string ArmIdField = "ArmId";
    internal const string PrincipalField = "PrincipalId";
    internal const string CommandIdField = "CommandId";
    internal const string ReadKindField = "ReadKind";
    internal const string PhaseField = "Phase";
    internal const string ActionField = "Action";
    internal const string RequestIdField = "RequestId";
    internal const string ArmFilePrefix = "arm-";
    internal const string ReleaseFilePrefix = "release-";
    internal const string MarkerFilePrefix = "marker-";
    internal const string OwnerChanged = "The private probe owner record changed.";
    internal const string InvalidControlEntry = "The private probe directory contains an invalid entry.";
    internal const string FileLimitExceeded = "The private probe file-count limit was exceeded.";
    internal const string ByteLimitExceeded = "The private probe byte limit was exceeded.";
    internal const string RecordLimitExceeded = "The private probe record limit was exceeded.";
    internal const string DuplicateControlFile = "The private probe control record already exists.";
    internal const string OwnershipConflict = "The private probe root is not exclusively owned.";
    internal const string PrivatePermissionsUnsupported = "The private probe requires Unix private file permissions.";
    internal const string InvalidArm = "The private probe arm is invalid.";
    internal const string InvalidRelease = "The private probe release is invalid.";
    internal const string AdmissionStopped = "The private probe fixture has stopped admission.";
    internal const string MarkerMismatch = "The private probe marker did not match signed RF3 discovery.";
    internal const string MarkerLimitExceeded = "The private probe marker limit was exceeded.";
    internal const string GateLimitExceeded = "The private probe active-gate limit was exceeded.";
    internal const string UnsettledGates = "Private probe gates have not settled.";
    internal const string MissingScenarioDeadline = "The private probe requires a bounded scenario deadline.";

    internal const string Node1Origin = "http://node1:8080";
    internal const string Node2Origin = "http://node2:8080";
    internal const string Node3Origin = "http://node3:8080";
    internal static IReadOnlyList<string> Nodes { get; } = Array.AsReadOnly(new[]
        { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3 });
}
