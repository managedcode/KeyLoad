namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

internal static class PhysicalOwnerDirectoryProtocol
{
    internal const int Version = 1;
    internal const int MaximumOwners = 2;
    internal const int VoterCount = 3;
    internal const int MaximumEndpointBytes = 2048;
    internal const int MaximumEncodedBytes = 65_536;
    internal const int MaximumRegistrationBytes = 16_384;
    internal const long InitialEpoch = 1;
    internal const long EmptyRevision = 0;
    internal const long RevisionStep = 1;
    internal const string Malformed = "The committed physical owner directory is malformed.";
    internal const string InvalidRegistration = "The physical owner registration is invalid.";
    internal const string AdministratorRequired = "Cluster administration is required.";
    internal const string ControlMismatch = "The physical owner directory does not match the control authority.";
    internal const string StaleRevision = "The physical owner directory revision is stale.";
    internal const string IdentityConflict = "The registered physical owner identity conflicts with its existing entry.";
    internal const string Capacity = "The physical owner directory is full.";
    internal const string RevisionExhausted = "The physical owner directory revision is exhausted.";
    internal const string Missing = "The physical owner directory is not initialized.";
}
