using KeyLoad.Core;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingProcessRecovery
{
    private const string MissingOriginalRoot = "The original recovery store root must be canonical, existing and nonempty.";

    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        var original = await CommandIdempotencyCrashData.ReadEvidenceAsync<Command<DatabaseCommand>>(root, LocalInboxProcessingCrashContract.OperationFile, token);
        var input = await CommandIdempotencyCrashData.ReadEvidenceAsync<MessageInspection>(root, LocalInboxProcessingCrashContract.InputFile, token);
        var position = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, LocalInboxProcessingCrashContract.PositionFile, token);
        var target = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitToken>(root, LocalInboxProcessingCrashContract.TargetFile, token);
        var collection = await CommandIdempotencyCrashData.ReadEvidenceAsync<Command<DatabaseCommand>>(root, LocalInboxProcessingCrashContract.CollectionFile, token);
        var lifetime = await LocalInboxProcessingRecoveryModels.LifetimeAsync(collection);
        var tail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, LocalInboxProcessingCrashContract.TailFile, token);
        LocalInboxProcessingRecoveryState? state = null;
        string[] image = [];
        var finalPosition = 0L;
        StoreIdentity? identity = null;
        (byte[] Key, byte[] Value)[] originalRows = [];
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
                await LocalInboxProcessingRecoveryImage.CutAsync(database, store, original, input, position, tail, outcome is not null, lifetime);
                var reconciled = database.Apply(original);
                await LocalInboxProcessingRecoveryReceipt.RequireAsync(store, original, reconciled.Get<CommitReceipt>(), target, tail, lifetime);
                originalRows = LocalInboxProcessingRecoveryOriginalRows.Capture(store, original, tail);
                await LocalInboxProcessingRecoveryImage.CutAsync(database, store, original, input, position, tail, committed: true, lifetime);
                if (outcome is not null)
                {
                    await LocalInboxProcessingRecoveryImage.ReplayAsync(database, store, original, outcome.Value);
                    await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
                }
                state = await LocalInboxProcessingRecoveryContinuation.RunAsync(database, store, original, reconciled, originalRows, lifetime);
                await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
                image = LocalInboxProcessingRecoveryImage.All(store);
                finalPosition = store.Position;
                identity = store.Identity;
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(identity);
        await VerifyColdAsync(root, state, image, finalPosition, identity, originalRows, lifetime, failures);
    }

    private static async Task VerifyColdAsync(string root, LocalInboxProcessingRecoveryState state,
        string[] image, long finalPosition, StoreIdentity identity, (byte[] Key, byte[] Value)[] originalRows,
        EntityRefLifetime lifetime, List<Exception> failures)
    {
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
                await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
                var database = TargetInboxRecoveryDatabase.Open(store);
                await VerifyContinuationAsync(database, store, state, originalRows, lifetime);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyContinuationAsync(KeyLoad.Core.DatabaseEngine database, ZoneTreeStore store,
        LocalInboxProcessingRecoveryState state, (byte[] Key, byte[] Value)[] originalRows, EntityRefLifetime lifetime)
    {
        foreach (var item in state.History)
        {
            await LocalInboxProcessingRecoveryImage.ReplayAsync(database, store, item.Operation, item.Result);
            await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
        }
        await LocalInboxProcessingRecoveryHealthy.ModelsAsync(database, lifetime);
        await LocalInboxProcessingRecoveryReceiveFence.RequireAsync(database, store, state);
        await LocalInboxProcessingRecoveryOriginalRows.SameAsync(store, originalRows);
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
