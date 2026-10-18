using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryContinuation
{
    private const long NewGeneration = 2L;
    private const string ConflictDetail = "The inbox input was already completed with different effects.";
    private const string StaleDetail = "The delivery lease is stale.";

    internal static async Task<LocalInboxProcessingRecoveryState> RunAsync(DatabaseEngine database, ZoneTreeStore store,
        Command<DatabaseCommand> original, Result<object> result, (byte[] Key, byte[] Value)[] originalRows)
    {
        var history = new List<(Command<DatabaseCommand>, Result<object>)> { (original, result) };
        var request = NativeSerialization.Deserialize<ProcessingRequest>(original.Value!.Input.Span, NativeValidationProfile.PublicInputElements);
        var duplicate = Operation(database, original, request with
        {
            CommandId = Guid.NewGuid()
        });
        var before = LocalInboxProcessingRecoveryDomain.Read(store);
        var dedup = database.Apply(duplicate);
        await LocalInboxProcessingRecoveryImage.ResultAsync(dedup, result);
        await LocalInboxProcessingRecoveryDomain.SameAsync(store, before);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        history.Add((duplicate, dedup));
        var changed = Operation(database, original, request with
        {
            CommandId = Guid.NewGuid(),
            Effects = [new PutDocument(LocalInboxProcessingCrashContract.Documents, "refused", LocalInboxProcessingCrashContract.Payload)]
        });
        history.Add((changed, await RefuseAsync(database, store, changed, ErrorCode.Conflict, ConflictDetail, originalRows)));
        var stale = Operation(database, original, request with
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

    internal static Command<DatabaseCommand> Operation(DatabaseEngine database, Command<DatabaseCommand> original, ProcessingRequest request)
        => database.CreateCommand(original.Value!.Kind, request.CommandId, original.UserId!,
            new DateTimeOffset(original.Timestamp), JsonSerializer.Serialize(request, JsonDefaults.Options));

    private static async Task<Result<object>> RefuseAsync(DatabaseEngine database, ZoneTreeStore store,
        Command<DatabaseCommand> operation, ErrorCode code, string detail, (byte[] Key, byte[] Value)[] originalRows)
    {
        var before = LocalInboxProcessingRecoveryDomain.Read(store);
        var result = database.Apply(operation);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(code);
        await Assert.That(result.Problem?.Detail).IsEqualTo(detail);
        await Assert.That(result.Value).IsNull();
        await LocalInboxProcessingRecoveryDomain.SameAsync(store, before);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        await LocalInboxProcessingRecoveryImage.ReplayAsync(database, store, operation, result);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        return result;
    }
}
