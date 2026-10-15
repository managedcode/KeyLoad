using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingProcessRecovery
{
    private const string MissingOriginalRoot = "The original recovery store root must be canonical, existing and nonempty.";

    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        var original = await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation>(root, LocalInboxProcessingCrashContract.OperationFile, token);
        var input = await CommandIdempotencyCrashData.ReadEvidenceAsync<MessageInspection>(root, LocalInboxProcessingCrashContract.InputFile, token);
        var position = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, LocalInboxProcessingCrashContract.PositionFile, token);
        var tail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, LocalInboxProcessingCrashContract.TailFile, token);
        LocalInboxProcessingRecoveryState? state = null;
        string[] image = [];
        var finalPosition = 0L;
        StoreIdentity? identity = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            RequireOriginalRoot(root);
            using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = TargetInboxRecoveryDatabase.Open(store);
                var outcome = OutcomeStoreOracle.Read(store, original);
                if (stage >= CommitStage.JournalFlushed)
                {
                    await Assert.That(outcome).IsNotNull();
                }
                await LocalInboxProcessingRecoveryImage.CutAsync(database, store, original, input, position, tail, outcome is not null);
                var reconciled = database.Apply(original);
                await LocalInboxProcessingRecoveryReceipt.RequireAsync(store, original, reconciled.Get<CommitReceipt>(), position + 1L, tail);
                await LocalInboxProcessingRecoveryImage.CutAsync(database, store, original, input, position, tail, committed: true);
                if (outcome is not null)
                {
                    await LocalInboxProcessingRecoveryImage.ReplayAsync(database, store, original, outcome);
                }
                state = await LocalInboxProcessingRecoveryContinuation.RunAsync(database, store, original, reconciled);
                image = LocalInboxProcessingRecoveryImage.All(store);
                finalPosition = store.Position;
                identity = store.Identity;
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(identity);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            RequireOriginalRoot(root);
            using var store = new ZoneTreeStore(new(root)
            {
                Incarnation = identity.Incarnation,
                SigningKey = identity.SigningKey
            }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await LocalInboxProcessingRecoveryImage.ExactAsync(store.Identity, identity);
                await Assert.That(store.Position).IsEqualTo(finalPosition);
                await Assert.That(LocalInboxProcessingRecoveryImage.All(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
                var database = TargetInboxRecoveryDatabase.Open(store);
                foreach (var item in state.History)
                {
                    await LocalInboxProcessingRecoveryImage.ReplayAsync(database, store, item.Operation, item.Result);
                }
                await LocalInboxProcessingRecoveryHealthy.ModelsAsync(database);
                await LocalInboxProcessingRecoveryReceiveFence.RequireAsync(database, store, state);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static void RequireOriginalRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root)
            || !string.Equals(Path.GetFullPath(root), root, StringComparison.Ordinal)
            || !System.IO.Directory.Exists(root)
            || !System.IO.Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException(MissingOriginalRoot);
        }
    }

}
