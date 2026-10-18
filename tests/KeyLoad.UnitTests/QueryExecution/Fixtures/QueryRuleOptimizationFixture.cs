using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryRuleOptimizationFixture(bool includeNull, bool includeMissing) : IDisposable
{
    private const long InitialRevision = 1L;
    private const long UpdatedRevision = 2L;
    private const long InitialPolicyEpoch = 1L;
    private const long JournalAdvance = 1L;

    internal TestDatabase Harness
    {
        get;
    } = new();
    internal List<(CommandRequest Command, CommitReceipt Receipt, byte[] Outcome)> Originals
    {
        get;
    } = [];
    internal async Task InitializeAsync()
    {
        var db = Harness;
        for (var collection = 0; collection < QueryRuleOptimizationData.Collections.Length; collection++)
        {
            var definition = QueryRuleOptimizationData.Resource(db.Partition, collection, includeNull, includeMissing);
            await RequiredProcessingPolicyAssertions.EqualAsync(definition, db.Submit(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition)).Get<ResourceDefinition>());
        }
        await CommitAsync(QueryRuleOptimizationData.Seed(db.Partition), InitialRevision);
        await CommitAsync(QueryRuleOptimizationData.Update(db.Partition), UpdatedRevision);
        await PolicyAsync(InitialPolicyEpoch, true);
    }
    private async Task CommitAsync(CommandRequest command, long revision)
    {
        var db = Harness;
        var position = db.ReadCanonicalCut().AppliedPosition + JournalAdvance;
        var expected = new CommitReceipt(command.CommandId, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId, position), [.. command.Mutations.Cast<PutDocument>().Select(row => new MutationReceipt("putDocument", row.Resource, row.Id, revision))], DurabilityProfile.ProcessDurable, 1);
        await RequiredProcessingPolicyAssertions.ReceiptAsync(expected, db.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>());
        Originals.Add((command, expected, RequiredProcessingPolicyAssertions.Outcome(db.Store, db.Partition, "root", command.CommandId)));
    }
    internal async Task PolicyAsync(long epoch, bool allow)
    {
        var expected = new PrincipalRecord(QueryRuleOptimizationData.Reader, Harness.Partition.TenantId,
            [.. QueryRuleOptimizationData.Collections.Select(collection => new ScopeGrant(Harness.Partition.DatabaseId, collection,
                Capability.Query | Capability.DocumentsRead))],
            allow ? [QueryRuleOptimizationData.ReadGrant, QueryRuleOptimizationData.UseGrant] : [QueryRuleOptimizationData.ReadGrant])
        {
            PolicyEpoch = epoch
        };
        await RequiredProcessingPolicyAssertions.EqualAsync(expected, Harness.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>());
    }
    public void Dispose() => Harness.Dispose();
}
