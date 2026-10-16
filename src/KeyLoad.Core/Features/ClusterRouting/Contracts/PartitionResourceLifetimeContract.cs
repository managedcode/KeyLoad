namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

internal static class PartitionResourceLifetimeContract
{
    internal const string CoverageAlias = "keyload.resource-lifetime-coverage.v1";
    internal const string ControlProofAlias = "keyload.partition-control-lifetime-proof.v1";
    internal const string MoveProofAlias = "keyload.partition-move-lifetime-proof.v1";
    internal const string CommandAlias = "keyload.partition-movement-authenticated-command.v1";

    internal const uint CoveragePartitionId = 0;
    internal const uint CoverageLifetimesId = 1;
    internal const uint ControlDelegationId = 0;
    internal const uint ControlCoverageId = 1;
    internal const uint MoveGrantId = 0;
    internal const uint MoveAuthorizationFingerprintId = 1;
    internal const uint MoveControlProofId = 2;
    internal const uint CommandOriginalId = 0;
    internal const uint CommandLifetimeProofsId = 1;
}
