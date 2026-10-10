using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineCapabilityResult
{
    private const long InitialThroughSequence = 0;
    private const int EmptyTrackedRecords = 0;
    internal static OnlineTextCapabilityResult Create(NativeTextOnlineSession session,
        OnlineTextIndexMaintenanceResult? original = null)
    {
        var budget = session.Budget;
        var result = new OnlineTextCapabilityResult(session.Id,
            session.Base is { } captured ? Cut(session.Request.NodeId, captured) : null,
            session.Current is { } current ? Cut(session.Request.NodeId, current) : null,
            session.Manifest?.ThroughSequence ?? session.Base?.Checkpoint ?? InitialThroughSequence,
            session.Manifest?.Records.Length ?? session.Base?.Documents.Length ?? EmptyTrackedRecords,
            session.Manifest is { } manifest ? Digest(manifest, budget) : null,
            original ?? session.JoinedPublicationReceipt, session.Issued, session.CheckpointIntent, session.ExpectedCurrentCommandId);
        budget.CheckResult(result);
        return result;
    }

    internal static TextIndexSourceCut Cut(Guid nodeId, NativeTextSeedCapture actual)
        => new(nodeId, actual.Incarnation, actual.Position, actual.AppliedPosition,
            actual.UpperSequence, actual.ReadGeneration, actual.SchemaVersion, actual.PolicyEpoch, actual.ResourceSha256);

    internal static string Digest(NativeTextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(NativeSerialization.Measure(manifest.Files));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        return Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(manifest.Files)));
    }
}
