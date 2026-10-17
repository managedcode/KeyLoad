using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryContinuation
{
    private const long NewGeneration = 2L;
    private const string ConflictDetail = "The inbox input was already completed with different effects.";
    private const string StaleDetail = "The delivery lease is stale.";

    internal static async Task<LocalInboxProcessingRecoveryState> RunAsync(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation original, OperationResult result, (byte[] Key, byte[] Value)[] originalRows)
    {
        var history = new List<(ReplicatedOperation, OperationResult)> { (original, result) };
        var request = JsonDefaults.Deserialize<ProcessingRequest>(Encoding.UTF8.GetBytes(original.PayloadJson));
        var duplicate = Operation(original, request with
        {
            CommandId = Guid.NewGuid()
        });
        var before = LocalInboxProcessingRecoveryDomain.Read(store);
        var dedup = database.Apply(duplicate);
        await LocalInboxProcessingRecoveryImage.ResultAsync(dedup, result);
        await LocalInboxProcessingRecoveryDomain.SameAsync(store, before);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        history.Add((duplicate, dedup));
        var changed = Operation(original, request with
        {
            CommandId = Guid.NewGuid(),
            Effects = [new PutDocument(LocalInboxProcessingCrashContract.Documents, "refused", LocalInboxProcessingCrashContract.Payload)]
        });
        history.Add((changed, await RefuseAsync(database, store, changed, ErrorCode.Conflict, ConflictDetail, originalRows)));
        var stale = Operation(original, request with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = NewGeneration,
            Effects = []
        });
        history.Add((stale, await RefuseAsync(database, store, stale, ErrorCode.StaleLease, StaleDetail, originalRows)));
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        var (receiveOperation, received) = await LocalInboxProcessingHealthyContinuation.RunAsync(database, store, original, history, originalRows);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        foreach (var entry in history)
        {
            await LocalInboxProcessingRecoveryImage.ReplayAsync(database, store, entry.Item1, entry.Item2);
            await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        }
        return new(history.ToArray(), receiveOperation, received);
    }

    internal static ReplicatedOperation Operation(ReplicatedOperation original, ProcessingRequest request)
        => original with
        {
            Id = request.CommandId,
            PayloadJson = JsonSerializer.Serialize(request, JsonDefaults.Options)
        };

    private static async Task<OperationResult> RefuseAsync(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation operation, ErrorCode code, string detail, (byte[] Key, byte[] Value)[] originalRows)
    {
        var before = LocalInboxProcessingRecoveryDomain.Read(store);
        var result = database.Apply(operation);
        await Assert.That(result.Error).IsEqualTo(code);
        await Assert.That(result.SafeDetail).IsEqualTo(detail);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        await LocalInboxProcessingRecoveryDomain.SameAsync(store, before);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        await LocalInboxProcessingRecoveryImage.ReplayAsync(database, store, operation, result);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        return result;
    }
}
