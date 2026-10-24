using System.Security.Cryptography;
using KeyLoad.Clustering.Search;
using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.Server.Search;

internal static class TextOnlineCapabilityResult
{
    private const long InitialThroughSequence = 0;
    private const int EmptyTrackedRecords = 0;
    internal static OnlineTextCapabilityResult Create(TextOnlineSession session,
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

    internal static TextIndexSourceCut Cut(Guid nodeId, TextSeedCapture actual)
        => new(nodeId, actual.Incarnation, actual.Position, actual.AppliedPosition,
            actual.UpperSequence, actual.ReadGeneration, actual.SchemaVersion, actual.PolicyEpoch, actual.ResourceSha256);

    internal static string Digest(TextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(OrleansSerialization.Measure(manifest.Files));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        return Convert.ToHexStringLower(SHA256.HashData(OrleansSerialization.Serialize(manifest.Files)));
    }
}
