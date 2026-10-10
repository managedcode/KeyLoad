using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalCapabilityResult
{
    internal static TextMaintenanceCapabilityResult Create(NativeTextIncrementalSession session,
        NativeTextSeedCapture observed)
    {
        var budget = session.Budget;
        budget.Check();
        var result = new TextMaintenanceCapabilityResult(session.Id,
            new(session.Request.NodeId, observed.Incarnation, observed.Position, observed.AppliedPosition,
                observed.UpperSequence, observed.ReadGeneration, observed.SchemaVersion, observed.PolicyEpoch,
                observed.ResourceSha256), session.Checkpoint,
            session.Manifest?.ThroughSequence ?? session.Checkpoint,
            session.Manifest?.Records.Length ?? observed.Documents.Length, session.Intent?.CheckpointCommand,
            session.Manifest is { } complete ? Digest(complete, budget) : null,
            session.CurrentReplayUpperSequence, session.Manifest?.LastSettledCheckpointRequest);
        budget.CheckResult(result);
        budget.Check();
        return result;
    }

    private static string Digest(NativeTextIncrementalManifest actual, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(NativeSerialization.Measure(actual.Files));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        var original = NativeSerialization.Serialize(actual.Files);
        budget.Check();
        var digest = Convert.ToHexStringLower(SHA256.HashData(original));
        budget.Check();
        return digest;
    }
}
