namespace KeyLoad.RecoveryTests.Features.Search;

/// <summary>Retains test evidence of the actual command and ACK, never projection authority.</summary>
internal static class TextIncrementalCheckpointEvidence
{
    internal static string CommandFile(Guid requestId) => FileName(
        TextIncrementalCrashProtocol.CheckpointCommandPrefix, requestId);
    internal static string ReceiptFile(Guid requestId) => FileName(
        TextIncrementalCrashProtocol.CheckpointReceiptPrefix, requestId);

    private static string FileName(string prefix, Guid requestId)
        => prefix + requestId.ToString(TextIncrementalCrashProtocol.EvidenceIdFormat)
            + TextIncrementalCrashProtocol.EvidenceSuffix;

    internal static async Task RecordCommandAsync(TextIncrementalCrashRuntime runtime,
        TextIndexMaintenanceRequest request, CommitProjectionBatchRequest actual, CancellationToken token)
    {
        if (request.Mode != TextIndexMaintenanceMode.Restore)
        {
            return;
        }
        var file = CommandFile(request.CommandId);
        if (File.Exists(Path.Combine(runtime.EvidenceRoot, file)))
        {
            var retained = await TextIncrementalEvidenceFiles.ReadAsync<CommitProjectionBatchRequest>(
                runtime.EvidenceRoot, file, token);
            if (!JsonDefaults.Serialize(retained).AsSpan().SequenceEqual(JsonDefaults.Serialize(actual)))
            {
                throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
            }
            return;
        }
        await TextIncrementalEvidenceFiles.WriteAsync(runtime.EvidenceRoot, file, actual, token);
    }

    internal static async Task RecordReceiptAsync(TextIncrementalCrashRuntime runtime,
        TextIndexMaintenanceRequest request, ProjectionBatchResult actual, CancellationToken token)
    {
        if (request.Mode != TextIndexMaintenanceMode.Restore)
        {
            return;
        }
        var file = ReceiptFile(request.CommandId);
        if (File.Exists(Path.Combine(runtime.EvidenceRoot, file)))
        {
            var retained = await TextIncrementalEvidenceFiles.ReadAsync<ProjectionBatchResult>(
                runtime.EvidenceRoot, file, token);
            if (!OrleansSerialization.Serialize(retained).AsSpan().SequenceEqual(OrleansSerialization.Serialize(actual)))
            {
                throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
            }
            return;
        }
        await TextIncrementalEvidenceFiles.WriteAsync(runtime.EvidenceRoot, file, actual, token);
    }

    internal static async Task<ProjectionBatchResult> ResolveOriginalAsync(TextIncrementalCrashRuntime runtime,
        TextIndexMaintenanceRequest request, ProjectionBatchResult? observed, CancellationToken token)
    {
        var original = await TextIncrementalEvidenceFiles.ReadAsync<CommitProjectionBatchRequest>(
            runtime.EvidenceRoot, CommandFile(request.CommandId), token);
        var applied = runtime.Database.LastApplied;
        var position = runtime.Database.Store.Position;
        var replay = await runtime.CommitAsync<ProjectionBatchResult>(OperationKind.CommitProjectionBatch,
            original, original.CommandId, token);
        var retained = await TextIncrementalEvidenceFiles.ReadAsync<ProjectionBatchResult>(
            runtime.EvidenceRoot, ReceiptFile(request.CommandId), token);
        if (runtime.Database.LastApplied != applied
            || runtime.Database.Store.Position != position
            || !OrleansSerialization.Serialize(replay).AsSpan().SequenceEqual(OrleansSerialization.Serialize(retained))
            || (observed is not null && !OrleansSerialization.Serialize(replay).AsSpan()
                .SequenceEqual(OrleansSerialization.Serialize(observed))))
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        return replay;
    }
}
