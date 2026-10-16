using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingQuery
{
    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Configure(state.Source, false, FirstEpoch);
        var row = ResourcePairBindingFaults.Install(state, Documents);
        var image = QueueWholeFlowStorage.Bytes(state.Source.Store);
        var position = state.Source.Store.Position;
        await RefusedAsync(state, Capability.VectorSearch, ErrorCode.PermissionDenied);
        await RefusedAsync(state, Capability.None, ErrorCode.Corruption);
        await ResourcePairBindingAssertions.UnchangedAsync(state.Source.Store, image, position);
        ResourcePairBindingFaults.Repair(state.Source.Store, row);
        await HealthyAsync(state, state.Source.Database, false);
        await RefusedAsync(state, Capability.VectorSearch, ErrorCode.PermissionDenied);
        Configure(state.Source, true, NextPolicyEpoch);
        await HealthyAsync(state, state.Source.Database, true);
        await BudgetAsync(state, token);
        await AbsentAsync(state);
        await ResourcePairBindingAssertions.PairsAsync(state, state.Source.Store);
    }

    private static void Configure(TestDatabase source, bool vector, long epoch)
    {
        ScopeGrant[] grants = vector
            ? [new(source.Partition.DatabaseId, Documents, Capability.Query | Capability.DocumentsRead),
                new(source.Partition.DatabaseId, Documents, Capability.VectorSearch)]
            : [new(source.Partition.DatabaseId, Documents, Capability.Query | Capability.DocumentsRead)];
        var principal = new PrincipalRecord(QueryUser, source.Partition.TenantId, [.. grants], []) { PolicyEpoch = epoch };
        _ = source.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }

    private static async Task RefusedAsync(ResourcePairBindingState state, Capability additional, ErrorCode expected)
    {
        var entered = false;
        DocumentResult Read(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource)
        {
            entered = true;
            return Project(state, state.Source.Database, view, principal, resource);
        }
        var failure = additional == Capability.None
            ? Assert.ThrowsExactly<KeyLoadException>(() => state.Source.Database.WithQueryView(QueryUser, state.Source.Partition, Documents, Read))
            : Assert.ThrowsExactly<KeyLoadException>(() => state.Source.Database.WithQueryView(QueryUser, state.Source.Partition, Documents, additional, Read));
        await Assert.That(failure.Code).IsEqualTo(expected);
        if (expected == ErrorCode.PermissionDenied)
        {
            await Assert.That(failure.Message).IsEqualTo(DeniedDetail);
        }
        await Assert.That(entered).IsFalse();
    }

    internal static async Task HealthyAsync(ResourcePairBindingState state, DatabaseEngine database, bool additional = true)
    {
        var entered = false;
        DocumentResult Read(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource)
        {
            entered = true;
            return Project(state, database, view, principal, resource);
        }
        var actual = additional
            ? database.WithQueryView(QueryUser, state.Source.Partition, Documents, Capability.VectorSearch, Read)
            : database.WithQueryView(QueryUser, state.Source.Partition, Documents, Read);
        await ResourcePairBindingAssertions.EqualAsync(new DocumentResult(new(state.Source.Partition, Documents,
            state.Mixed[Zero].Id), FirstRevision, DocumentJson, false, []), actual);
        await Assert.That(entered).IsTrue();
    }

    private static async Task BudgetAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var database = state.Source.Database;
        var image = QueueWholeFlowStorage.Bytes(state.Source.Store);
        var position = state.Source.Store.Position;
        var entered = false;
        DocumentResult Read(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource)
        {
            entered = true;
            return Project(state, database, view, principal, resource);
        }
        var baseline = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        var expected = database.WithQueryView(QueryUser, state.Source.Partition, Documents, Capability.VectorSearch, Read, baseline);
        await Assert.That(entered).IsTrue();
        var lifetimeCharge = ResourcePairBindingFaults.LifetimeKey(state.Source.Partition, Documents).Length
            + (long)state.Pairs[Documents].Lifetime.Length;
        await Assert.That(baseline.ReadBytes).IsGreaterThan(lifetimeCharge);
        var limits = UnitExecutionOptions.DatabaseLimits(new()
        {
            MaxQueryReadBytes = baseline.ReadBytes - One
        });
        var limited = new ReadExecutionBudget(limits, database.EvaluationClock, token);
        entered = false;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.WithQueryView(QueryUser, state.Source.Partition,
            Documents, Capability.VectorSearch, Read, limited));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(entered).IsFalse();
        await Assert.That(limited.ReadBytes).IsEqualTo(baseline.ReadBytes - lifetimeCharge);
        await Assert.That(limited.ConstrainedNativeRecords).IsEqualTo(baseline.ConstrainedNativeRecords - One);
        var healthy = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        await ResourcePairBindingAssertions.EqualAsync(expected, database.WithQueryView(QueryUser, state.Source.Partition,
            Documents, Capability.VectorSearch, Read, healthy));
        await Assert.That(healthy.ReadBytes).IsEqualTo(baseline.ReadBytes);
        await Assert.That(healthy.ConstrainedNativeRecords).IsEqualTo(baseline.ConstrainedNativeRecords);
        await ResourcePairBindingAssertions.UnchangedAsync(state.Source.Store, image, position);
    }

    private static async Task AbsentAsync(ResourcePairBindingState state)
    {
        var partition = state.Source.Partition;
        var definitionKey = KeySpace.Resource(partition.TenantId, partition.DatabaseId, Documents);
        var lifetimeKey = ResourcePairBindingFaults.LifetimeKey(partition, Documents);
        state.Source.Store.Commit((transaction, _) => { transaction.Delete(definitionKey); transaction.Delete(lifetimeKey); return true; });
        var image = QueueWholeFlowStorage.Bytes(state.Source.Store);
        var position = state.Source.Store.Position;
        await RefusedAsync(state, Capability.VectorSearch, ErrorCode.NotFound);
        await ResourcePairBindingAssertions.UnchangedAsync(state.Source.Store, image, position);
        state.Source.Store.Commit((transaction, _) =>
        {
            transaction.Put(definitionKey, state.Pairs[Documents].Definition);
            transaction.Put(lifetimeKey, state.Pairs[Documents].Lifetime);
            return true;
        });
        await HealthyAsync(state, state.Source.Database);
    }

    private static DocumentResult Project(ResourcePairBindingState state, DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource)
    {
        var reference = new EntityRef(state.Source.Partition, Documents, state.Mixed[Zero].Id);
        var document = database.ReadVisibleDocument(view, principal, reference) ?? throw new InvalidOperationException(Invalid);
        return database.Project(principal, resource, document);
    }
}
