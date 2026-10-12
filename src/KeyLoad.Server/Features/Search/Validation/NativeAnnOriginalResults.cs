using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnOriginalResults
{
    private const int Empty = 0;
    private const int DigestCharacters = 64;

    internal static bool SameRequest(AnnMaintenanceRequest first, AnnMaintenanceRequest second)
        => global::KeyLoad.NativeSerialization.Serialize(first).AsSpan()
            .SequenceEqual(global::KeyLoad.NativeSerialization.Serialize(second));

    internal static void Require(NativeAnnOriginalOperation entry)
    {
        if (entry is null || entry.Key is null || string.IsNullOrEmpty(entry.PrincipalId)
            || entry.PolicyEpoch <= Empty || entry.PendingStarted && entry.PendingRequest is null
            || (entry.LastRequest is null) != (entry.LastResult is null)
            || entry.PendingRequest is null && entry.LastRequest is null
            || entry.InitialRequest is null || entry.InitialSettled && entry.LastResult is null)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        RequireRetired(entry);
        RequireRequest(entry.Key, entry.InitialRequest);
        if (entry.InitialSettlementIntent is { } intent && (!entry.InitialSettled
            || intent.Consumer != entry.Key.Consumer || intent.CommandId == Guid.Empty
            || intent.Effects.IsDefault || intent.Effects.Length != Empty
            || string.IsNullOrEmpty(intent.Token) || string.IsNullOrEmpty(intent.InitialAnnMaintenanceSettlement)))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        if (!entry.InitialSettled && (entry.LastResult is not null
            || entry.PendingRequest is null || !SameRequest(entry.InitialRequest, entry.PendingRequest)))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        if (entry.PendingRequest is { } pending) { RequireRequest(entry.Key, pending); }
        if (entry.LastRequest is { } settled && entry.LastResult is { } result)
        { RequireRequest(entry.Key, settled); RequireResult(settled, result); }
    }

    private static void RequireRetired(NativeAnnOriginalOperation entry)
    {
        if (entry.RetiredOriginals is null || entry.RetiredOriginals.Any(value => value is null)
            || entry.RetiredOriginals.Select(value => value.CommandId).Distinct().Count() != entry.RetiredOriginals.Length
            || entry.RetiredOriginals.Any(value => value.CommandId == Guid.Empty
                || value.RequestSha256 is null || value.RequestSha256.Length != System.Security.Cryptography.SHA256.HashSizeInBytes
                || value.CommandId == entry.LastRequest?.CommandId || value.CommandId == entry.PendingRequest?.CommandId))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
    }

    internal static void RequireRequest(NativeAnnOwnedKey key, AnnMaintenanceRequest request)
    {
        if (request.CommandId == Guid.Empty || request.Consumer != key.Consumer
            || request.IndexGeneration != key.IndexGeneration || request.Mode == AnnMaintenanceMode.Release
            || !Enum.IsDefined(request.Mode))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
    }

    internal static void RequireResult(AnnMaintenanceRequest request, AnnMaintenanceResult result)
    {
        if (result is null || result.CommandId != request.CommandId || result.Consumer != request.Consumer
            || result.IndexGeneration != request.IndexGeneration || result.Phase != AnnMaintenancePhase.Completed
            || result.Source is null || result.Count < Empty || result.IndexSha256 is not { Length: DigestCharacters } digest
            || digest.Any(value => !char.IsAsciiHexDigitLower(value))
            || result.Source.NodeId != request.NodeId || result.Source.Incarnation != request.Placement.Incarnation
            || result.Checkpoint is { } checkpoint && checkpoint.Checkpoint != result.Source.ThroughSequence)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
    }

    internal static void RequirePublication(AnnMaintenanceResult result, NativeAnnManifest publication, AnnSeed current)
    {
        var source = result.Source ?? throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt);
        var stored = publication.Source;
        if (source.ThroughSequence != stored.ThroughSequence || source.PolicyEpoch != stored.PolicyEpoch
            || source.SchemaVersion != stored.SchemaVersion || source.CorpusSha256 != stored.CorpusSha256
            || source.DependencySha256 != stored.DependencySha256 || source.NodeId != stored.NodeId
            || source.Incarnation != stored.Incarnation || source.StoreFormatVersion != stored.StoreFormatVersion
            || source.KeyCodecVersion != stored.KeyCodecVersion || source.OutboxFirstAvailable != stored.OutboxFirstAvailable
            || source.Position < stored.Position || source.AppliedPosition < stored.AppliedPosition
            || source.Position > current.Cut.Position || source.AppliedPosition > current.Cut.AppliedPosition
            || source.ThroughSequence != current.ProjectionCheckpoint)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.InvalidSource); }
    }

    internal static void RequireCurrent(NativeAnnOriginalOperation entry, PrincipalRecord principal,
        AnnMaintenanceRequest request, AnnSeed current)
    {
        NativeAnnCanonicalSource.RequireRequest(request, current);
        if (entry.PrincipalId != principal.Id || entry.PolicyEpoch != current.Scope.PolicyEpoch
            || principal.PolicyEpoch != current.Scope.PolicyEpoch || !principal.ClusterAdministrator || principal.Revoked)
        { throw Errors.Fail(ErrorCode.PermissionDenied, NativeAnnProtocol.InvalidSource); }
        if (entry.LastResult?.Source is { } original && (original.NodeId != current.Cut.NodeId
            || original.Incarnation != current.Cut.Incarnation || original.PolicyEpoch != current.Scope.PolicyEpoch
            || original.SchemaVersion != current.Scope.SchemaVersion
            || original.StoreFormatVersion != current.Cut.StoreFormatVersion
            || original.KeyCodecVersion != current.Cut.KeyCodecVersion
            || original.Position > current.Cut.Position || original.AppliedPosition > current.Cut.AppliedPosition
            || original.ThroughSequence > current.Cut.OutboxTail))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource); }
    }
}
