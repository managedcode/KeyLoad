using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorCurrentSourceAuthorization
{
    private const int FirstOrdinal = 0;
    private const string Invalid = "The current event vector source authorization is inconsistent.";

    internal static EventVectorCoverageWitness Require(IKeyValueView bounded, PrincipalRecord principal,
        EventFeedControlRequest request, ImmutableArray<EventVectorAuthorizationScope> originalScopes,
        ReadOnlyMemory<byte> encoded, EventVectorAdmissionPolicy admission, EventVectorReadLifetimeBinding? lifetimeBinding = null)
    {
        if (originalScopes.IsDefault)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        admission.RequireEntryCount(originalScopes.Length);
        var fresh = EventVectorCoverageWitnessEncoding.Read(encoded, request, admission);
        var directory = PhysicalOwnerDirectorySerialization.Read(bounded)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        PhysicalOwnerDirectoryValidation.Validate(directory);
        if (fresh.OriginalPackets.Length != directory.Owners.Length
            || fresh.FreshPackets.Length != directory.Owners.Length)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
        var covered = new SortedDictionary<string, EventVectorAuthorizationScope>(StringComparer.Ordinal);
        var semantic = NativeSerialization.Deserialize<EventVectorCoverageSemanticIdentity>(
            fresh.OriginalSemanticProjectionBytes.Span);
        for (var ordinal = FirstOrdinal; ordinal < fresh.FreshPackets.Length; ordinal++)
        {
            RequireSamePacket(fresh.OriginalPackets[ordinal], fresh.FreshPackets[ordinal]);
            var capture = NativeSerialization.Deserialize<EventVectorCoverageCapture>(
                fresh.FreshPackets[ordinal].OriginalCaptureBytes.Span);
            if (capture.PrincipalId != principal.Id || capture.GroupOrdinal != ordinal
                || !PhysicalOwnerEntryValidation.SameOwner(capture.SourceOwner, directory.Owners[ordinal].Owner))
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, Invalid);
            }
            EventVectorCurrentCaptureDirectory.Require(directory, capture, admission);
            foreach (var entry in EventVectorEntryEncoding.Decode(capture.OriginalEncodedEntries, capture.EntryChecksum, admission))
            {
                if (entry.PrincipalId != capture.PrincipalId || entry.PolicyEpoch != capture.PolicyEpoch)
                {
                    throw Errors.Fail(ErrorCode.PermissionDenied, Invalid);
                }
            }
        }
        EventVectorParentCaptureScopes.Add(covered, semantic, request.Scope, admission);
        foreach (var scope in originalScopes)
        {
            if (!covered.ContainsValue(scope))
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, Invalid);
            }
        }
        lifetimeBinding?.Witness(fresh, admission);
        return fresh;
    }

    private static void RequireSamePacket(EventVectorCoveragePacket original, EventVectorCoveragePacket fresh)
    {
        if (!original.RequestBytes.Span.SequenceEqual(fresh.RequestBytes.Span)
            || !original.RequestSignature.Span.SequenceEqual(fresh.RequestSignature.Span)
            || !original.ReplyBytes.Span.SequenceEqual(fresh.ReplyBytes.Span)
            || !original.ReplySignature.Span.SequenceEqual(fresh.ReplySignature.Span)
            || !original.OriginalCaptureBytes.Span.SequenceEqual(fresh.OriginalCaptureBytes.Span))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Invalid);
        }
    }

    internal static void RequireOriginalFences(EventVectorControlPhase original, EventVectorCoverageWitness fresh,
        EventVectorAdmissionPolicy admission)
    {
        var retained = EventVectorCoverageWitnessEncoding.Read(original.OriginalCoverageWitness, original.OriginalRequest, admission);
        var expected = NativeSerialization.Deserialize<EventVectorCoverageSemanticIdentity>(retained.OriginalSemanticProjectionBytes.Span);
        var current = NativeSerialization.Deserialize<EventVectorCoverageSemanticIdentity>(fresh.OriginalSemanticProjectionBytes.Span);
        var actual = EventVectorEntryEncoding.Decode(current.OriginalEncodedEntries,
            EventVectorEntryEncoding.Digest(current.OriginalEncodedEntries), admission);
        foreach (var entry in EventVectorEntryEncoding.Decode(expected.OriginalEncodedEntries,
            EventVectorEntryEncoding.Digest(expected.OriginalEncodedEntries), admission))
        {
            var found = actual.SingleOrDefault(candidate => candidate.Source == entry.Source);
            if (found is null || found.PrincipalId != entry.PrincipalId || found.Incarnation != entry.Incarnation
                || found.PlacementEpoch != entry.PlacementEpoch || found.SchemaVersion != entry.SchemaVersion
                || found.LogicalPlacementRevision != entry.LogicalPlacementRevision || found.DirectoryFence != entry.DirectoryFence)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
            }
        }
    }
}
