using KeyLoad.Core;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoveryOracle
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        var input = await SharedDomainRecoveryInputReader.ReadAsync(root, token);
        var state = await SharedDomainRecoveryOwner.UseAsync(root, input.OriginalIdentityDigest,
            (database, store) => RecoverAsync(database, store, input, stage, token));
        _ = await SharedDomainRecoveryOwner.UseAsync(root, state.IdentityDigest, async (database, store) =>
        {
            await SharedDomainRecoveryImage.SameAsync(store, state.Image, state.Position);
            await SharedDomainRecoveryModels.RequireAsync(database, input, committed: true, healthy: true, token);
            await SharedDomainRecoveryHistoryAssertions.AllAsync(database, store, state.History);
            await SharedDomainRecoveryImage.SameAsync(store, state.Image, state.Position);
            return state;
        });
    }

    private static async Task<SharedDomainRecoveryRunState> RecoverAsync(DatabaseEngine database, ZoneTreeStore store,
        SharedDomainRecoveryInput input, CommitStage stage, CancellationToken token)
    {
        var original = SharedDomainCorpus.Create(Tenant, Database)[FirstIndex];
        var recovered = OutcomeStoreOracle.Read(store, input.Original);
        var committed = recovered is not null;
        if (stage is CommitStage.JournalFlushed or CommitStage.MutationApplied or CommitStage.ApplyCompleted)
        {
            await Assert.That(committed).IsTrue();
        }
        await Assert.That(store.Position).IsEqualTo(input.SeedPosition + (committed ? FirstRevision : NoRevision));
        await SharedDomainRecoveryModels.RequireAsync(database, input, committed, healthy: false, token);
        await SharedDomainRecoveryImage.SeedOtherScopesAsync(store, input, committed);
        var history = await SharedDomainRecoverySeed.VerifyAsync(database, store, input);
        var applied = database.Apply(input.Original);
        await SharedDomainRecoveryValues.ProducerAsync(applied, original, CommandId, Item, store.Identity.Incarnation,
            input.SeedPosition + FirstRevision);
        if (recovered is not null)
        {
            await SharedDomainRecoveryValues.ResultAsync(applied, recovered);
        }
        history.Add(SharedDomainRecoverySeed.Capture(store, input.Original, applied, original.Partition));
        await SharedDomainRecoveryImage.SeedOtherScopesAsync(store, input, committed: true);
        await SharedDomainRecoveryModels.RequireAsync(database, input, committed: true, healthy: false, token);
        await SharedDomainRecoveryHistoryAssertions.AllAsync(database, store, history);
        await SharedDomainRecoveryConflict.RequireAsync(database, store, input);
        await HealthyAsync(database, store, input, history, token);
        await SharedDomainRecoveryHistoryAssertions.AllAsync(database, store, history);
        return new(history.ToArray(), SharedDomainStoredImage.Capture(store), store.Position, input.OriginalIdentityDigest);
    }

    private static async Task HealthyAsync(DatabaseEngine database, ZoneTreeStore store, SharedDomainRecoveryInput input,
        List<SharedDomainRecoveryHistory> history, CancellationToken token)
    {
        var scopes = SharedDomainCorpus.Create(Tenant, Database);
        var id = Guid.NewGuid();
        foreach (var scope in scopes.Take(DocumentScopeIndex))
        {
            token.ThrowIfCancellationRequested();
            var others = scopes.Where(candidate => candidate != scope).ToDictionary(candidate => candidate.Partition,
                candidate => SharedDomainStoredImage.Capture(store, candidate.Partition));
            var command = SharedDomainCorpus.Producer(scope, id, Healthy);
            var operation = SharedDomainCrashOperations.Create(OperationKind.Batch, id, command, input.Original.EvaluatedAt);
            var position = store.Position + FirstRevision;
            var result = database.Apply(operation);
            await SharedDomainRecoveryValues.ProducerAsync(result, scope, id, Healthy, store.Identity.Incarnation, position);
            history.Add(SharedDomainRecoverySeed.Capture(store, operation, result, scope.Partition));
            foreach (var pair in others)
            {
                await Assert.That(SharedDomainStoredImage.Capture(store, pair.Key))
                    .IsEquivalentTo(pair.Value, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            }
        }
        await SharedDomainRecoveryModels.RequireAsync(database, input, committed: true, healthy: true, token);
    }
}
