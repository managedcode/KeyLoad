namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

internal static class TenantPlacementCapacityProtocol
{
    internal const string ReadPurpose = "keyload.owner-capacity.read.v1";
    internal const string Unavailable = "The original bounded owner capacity sample is unavailable.";
    internal const string InvalidProof = "The original owner capacity authority is invalid.";
    internal const string WrongCohort = "The original complete owner cohort changed.";
    internal const string Incomplete = "The complete bounded placement accounting did not finish.";
}
