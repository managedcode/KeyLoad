namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

internal static class TenantPlacementPolicyProtocol
{
    internal const int Version = 1;
    internal const int MaximumMoves = 64;
    internal const int MinimumMoves = 1;
    internal const int MinimumWeight = 1;
    internal const int MaximumWeight = 16;
    internal const int BasisPoints = 10_000;
    internal const int NoCompletedMoves = 0;
    internal const long EmptyRevision = 0;
    internal const long RevisionStep = 1;
    internal const string PolicySpace = "tenant-placement-policy-v1";
    internal const string IntentSpace = "tenant-placement-intent-v1";
    internal const string CapacitySpace = "placement-capacity-v1";
    internal const string PlanPurpose = "tenant-placement-plan-v1";
    internal const string Invalid = "The tenant placement policy or plan is invalid.";
    internal const string Missing = "The tenant placement policy is absent.";
    internal const string Malformed = "The committed tenant placement authority is malformed.";
    internal const string Stale = "The tenant placement authority revision changed.";
    internal const string AdministratorRequired = "Tenant placement requires persisted cluster administration.";
    internal const string ExpiredPlan = "The original tenant placement plan expired.";
    internal const string ScopeMismatch = "The tenant placement plan no longer has its original authority.";
}
