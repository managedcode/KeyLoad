namespace KeyLoad.CrashHost.Features.Search;

/// <summary>Retains test evidence of the actual command and ACK, never projection authority.</summary>
internal static class NativeTextIncrementalCheckpointEvidence
{
    internal static string CommandFile(Guid requestId) => FileName(
        NativeTextIncrementalCrashProtocol.CheckpointCommandPrefix, requestId);
    internal static string ReceiptFile(Guid requestId) => FileName(
        NativeTextIncrementalCrashProtocol.CheckpointReceiptPrefix, requestId);

    private static string FileName(string prefix, Guid requestId)
        => prefix + requestId.ToString(NativeTextIncrementalCrashProtocol.EvidenceIdFormat)
            + NativeTextIncrementalCrashProtocol.EvidenceSuffix;

    internal static async Task RecordCommandAsync(NativeTextIncrementalCrashRuntime runtime,
        TextIndexMaintenanceRequest request, CommitProjectionBatchRequest actual, CancellationToken token)
    {
        if (request.Mode != TextIndexMaintenanceMode.Restore)
        { return; }
        var file = CommandFile(request.CommandId);
        if (File.Exists(Path.Combine(runtime.EvidenceRoot, file)))
        {
            var retained = await NativeTextIncrementalEvidenceFiles.ReadAsync<CommitProjectionBatchRequest>(
                runtime.EvidenceRoot, file, token);
            if (!JsonDefaults.Serialize(retained).AsSpan().SequenceEqual(JsonDefaults.Serialize(actual)))
            { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
            return;
        }
        await NativeTextIncrementalEvidenceFiles.WriteAsync(runtime.EvidenceRoot, file, actual, token);
    }

    internal static async Task RecordReceiptAsync(NativeTextIncrementalCrashRuntime runtime,
        TextIndexMaintenanceRequest request, ProjectionBatchResult actual, CancellationToken token)
    {
        if (request.Mode != TextIndexMaintenanceMode.Restore)
        { return; }
        var file = ReceiptFile(request.CommandId);
        if (File.Exists(Path.Combine(runtime.EvidenceRoot, file)))
        {
            var retained = await NativeTextIncrementalEvidenceFiles.ReadAsync<ProjectionBatchResult>(
                runtime.EvidenceRoot, file, token);
            if (!NativeSerialization.Serialize(retained).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual)))
            { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
            return;
        }
        await NativeTextIncrementalEvidenceFiles.WriteAsync(runtime.EvidenceRoot, file, actual, token);
    }

    internal static async Task<ProjectionBatchResult> ResolveOriginalAsync(NativeTextIncrementalCrashRuntime runtime,
        TextIndexMaintenanceRequest request, ProjectionBatchResult? observed, CancellationToken token)
    {
        var original = await NativeTextIncrementalEvidenceFiles.ReadAsync<CommitProjectionBatchRequest>(
            runtime.EvidenceRoot, CommandFile(request.CommandId), token);
        var before = runtime.Node.Log.State.LastIndex;
        var applied = runtime.Database.LastApplied;
        var position = runtime.Database.Store.Position;
        var replay = await runtime.CommitAsync<ProjectionBatchResult>(OperationKind.CommitProjectionBatch,
            original, original.CommandId, token);
        var retained = await NativeTextIncrementalEvidenceFiles.ReadAsync<ProjectionBatchResult>(
            runtime.EvidenceRoot, ReceiptFile(request.CommandId), token);
        if (runtime.Node.Log.State.LastIndex != before || runtime.Database.LastApplied != applied
            || runtime.Database.Store.Position != position
            || !NativeSerialization.Serialize(replay).AsSpan().SequenceEqual(NativeSerialization.Serialize(retained))
            || (observed is not null && !NativeSerialization.Serialize(replay).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(observed))))
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        return replay;
    }
}
