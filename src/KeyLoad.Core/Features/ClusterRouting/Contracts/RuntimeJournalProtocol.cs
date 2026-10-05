namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

internal static class RuntimeJournalProtocol
{
    internal const int CurrentVersion = 1;
    internal const long InitialOwnerGeneration = 1;
    internal const long InitialContentRevision = 0;
    internal const long InitialLength = 0;
    internal const int InitialJournalCount = 0;
    internal const long InitialTotalBytes = 0;
    internal const long ProtectedPolicyEpoch = 1;
    internal const string GuidFormat = "N";
    internal const string Space = "runtime-journal";
    internal const string IdentityId = "keyload-internal-runtime-journal";
    internal const string SystemTenant = "system";
    internal const string MarkerKey = "catalog";
    internal const string QuotaKey = "quota";
    internal const string HeaderKey = "header";
    internal const string ChunkKey = "chunk";
    internal const string OwnerProperty = "DurableJobsOwner";
    internal const string PoisonedProperty = "DurableJobsPoisoned";
    internal const string ClosedProperty = "DurableJobsClosed";
    internal const string MembershipProperty = "DurableJobsMembershipVersion";
    internal const string AdoptedProperty = "DurableJobsAdoptedCount";
    internal const string MetadataPrefix = "DurableJobsMetadata_";

    internal const string InvalidRequest = "The runtime journal request is invalid.";
    internal const string InvalidState = "The committed runtime journal state is malformed.";
    internal const string Missing = "The runtime journal does not exist.";
    internal const string Conflict = "The runtime journal changed since it was observed.";
    internal const string Capacity = "The runtime journal capacity is exhausted.";
}
