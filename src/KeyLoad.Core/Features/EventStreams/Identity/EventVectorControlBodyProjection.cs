using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorControlBodyProjection
{
    private const int Version = 1;

    internal static ReadOnlyMemory<byte> Digest(EventVectorControlPhase phase, EventVectorAdmissionPolicy admission)
    {
        var identity = Project(phase, admission);
        admission.RequireEncodedBytes(NativeSerialization.Measure(identity));
        var bytes = NativeSerialization.Serialize(identity);
        admission.RequireEncodedBytes(bytes.Length);
        return SHA256.HashData(bytes);
    }

    internal static EventVectorControlBodyIdentity Project(EventVectorControlPhase phase,
        EventVectorAdmissionPolicy admission)
    {
        EventVectorControlFields.Require(phase, admission);
        var witness = EventVectorCoverageWitnessEncoding.Read(phase.OriginalCoverageWitness,
            phase.OriginalRequest, admission);
        var source = phase.IntendedSourcePhase ?? phase.ObservedSourcePhase;
        return new EventVectorControlBodyIdentity(Version, Request(phase.OriginalRequest),
            EventVectorIdentityInputs.Digest(phase.OriginalEncodedEntries),
            EventVectorIdentityInputs.Digest(witness.OriginalSemanticDigest),
            EventVectorIdentityInputs.Digest(source?.OriginalNativeBody ?? ReadOnlyMemory<byte>.Empty),
            source?.OriginalPosition, source?.ControlPolicyEpoch)
        {
            OriginalParentExpiresAt = phase.OriginalParentExpiresAt,
            OriginalCleanupFrontierSemanticDigest = EventVectorIdentityInputs.Digest(
                EventVectorCleanupFrontierIdentity.Digest(phase.OriginalCleanupFrontierBytes, admission))
        };
    }

    internal static ReadOnlyMemory<byte> RequestDigest(EventFeedControlRequest original,
        EventVectorAdmissionPolicy admission)
    {
        var value = Request(original);
        admission.RequireEncodedBytes(NativeSerialization.Measure(value));
        var bytes = NativeSerialization.Serialize(value);
        admission.RequireEncodedBytes(bytes.Length);
        return SHA256.HashData(bytes);
    }

    private static EventFeedControlRequest Request(EventFeedControlRequest original) => new()
    {
        Version = original.Version,
        CommandId = original.CommandId,
        ControlPartition = EventVectorIdentityInputs.Partition(original.ControlPartition),
        MapId = original.MapId,
        Action = original.Action,
        Scope = new EventFeedScope
        {
            Domain = EventVectorIdentityInputs.Text(original.Scope.Domain),
            Resource = EventVectorIdentityInputs.Text(original.Scope.Resource),
            Kind = original.Scope.Kind
        },
        Start = original.Start,
        ExpectedRevision = original.ExpectedRevision,
        ExpectedCoverageGeneration = original.ExpectedCoverageGeneration,
        Cursor = NullableText(original.Cursor),
        OfferDigest = NullableText(original.OfferDigest),
        Limit = original.Limit
    };

    private static string? NullableText(string? value)
        => value is null ? null : EventVectorIdentityInputs.Text(value);
}
