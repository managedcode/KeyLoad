using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorSourcePhaseIds
{
    internal const long InitialCleanupGeneration = 0;
    private const int Version = 1;
    private const int InitialSourceOrdinal = 0;
    private const long InitialRevision = 1;
    private const int IdentityBytes = 16;
    private const string InvalidIdentity = "The original event vector source identity is inconsistent.";

    internal static Guid For(EventVectorSourcePhase phase, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(admission);
        if (phase.Version != Version || phase.MapId == Guid.Empty || phase.ControlPartition is null
            || phase.ControlIncarnation == Guid.Empty || phase.MapRevision < InitialRevision
            || phase.CoverageGeneration < InitialRevision || string.IsNullOrWhiteSpace(phase.PrincipalId)
            || !Enum.IsDefined(phase.Role) || phase.SourceOrdinal < InitialSourceOrdinal
            || phase.OriginalBodyDigest.Length != SHA256.HashSizeInBytes
            || phase.CleanupGeneration < InitialCleanupGeneration
            || phase.CleanupGeneration != InitialCleanupGeneration && phase.Role != EventVectorSourcePhaseRole.Release)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidIdentity); }
        var identity = new EventVectorSourceIdentity(Version, phase.MapId, EventVectorIdentityInputs.Partition(phase.ControlPartition),
            phase.ControlIncarnation, phase.MapRevision, phase.CoverageGeneration, EventVectorIdentityInputs.Text(phase.PrincipalId),
            phase.Role, phase.SourceOrdinal, EventVectorIdentityInputs.Digest(phase.OriginalBodyDigest), phase.CleanupGeneration);
        admission.RequireEncodedBytes(NativeSerialization.Measure(identity));
        var encoded = NativeSerialization.Serialize(identity);
        admission.RequireEncodedBytes(encoded.Length);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(encoded, digest);
        return new Guid(digest[..IdentityBytes]);
    }
}
