namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireProbeProtocol
{
    internal const int Version = 1;
    internal const int IdentityCount = 10;
    internal const int CommonFaultCount = 11;
    internal const int MaximumFaultCount = 13;
    internal const string OwnerFile = "owner.json";
    internal const string ArmsFile = "arms.json";
    internal const string PublicationFile = "publication.lock";
    internal const string ClaimPrefix = "claim-";
    internal const string ResultPrefix = "result-";
    internal const string CompletePrefix = "complete-";
    internal const string SlotPrefix = "gate-";
    internal const string JsonSuffix = ".json";
    internal const string LockSuffix = ".lock";
    internal const string OrdinalFormat = "D2";
    internal const string SlotFormat = "D1";
    internal const string InvalidOwner = "NativeBlobWireOwnerInvalid";
    internal const string InvalidEvidence = "NativeBlobWireEvidenceInvalid";
    internal const string Contention = "NativeBlobWireOwnerContention";
    internal const string Incomplete = "NativeBlobWireCatalogIncomplete";
}
