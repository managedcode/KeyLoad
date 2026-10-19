using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryCapabilityVersionFixture : IDisposable
{
    internal TestDatabase Harness { get; } = new(QueryCapabilityVersionRequests.Limits());
    internal ResourceLifetime Creation { get; private set; } = null!;
    internal CommandRequest Seed { get; private set; } = null!;
    internal CommitReceipt Receipt { get; private set; } = null!;
    internal byte[] Outcome { get; private set; } = null!;
    internal async Task InitializeAsync()
    {
        var db = Harness;
        var definition = new ResourceDefinition(QueryCapabilityVersionRequests.Collection, ResourceKind.Collection, db.Partition.TransactionDomainId);
        await RequiredProcessingPolicyAssertions.EqualAsync(definition, db.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition)).Get<ResourceDefinition>());
        Creation = db.Store.Read(view => ResourceLifetimeCatalog.Read(view, db.Partition.TenantId,
            db.Partition.DatabaseId, definition.Name, definition))
            ?? throw new InvalidOperationException("The created query resource lifetime is absent.");
        Seed = QueryCapabilityVersionRequests.Seed(db.Partition);
        var next = db.ReadCanonicalCut().AppliedPosition + 1L;
        Receipt = new(Seed.CommandId, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId, next), [new("putDocument", QueryCapabilityVersionRequests.Collection, "a", 1L), new("putDocument", QueryCapabilityVersionRequests.Collection, "b", 1L),
             new("putDocument", QueryCapabilityVersionRequests.Collection, "c", 1L), new("putDocument", QueryCapabilityVersionRequests.Collection, "d", 1L)], DurabilityProfile.ProcessDurable, 1);
        await RequiredProcessingPolicyAssertions.ReceiptAsync(Receipt, db.Submit(OperationKind.Batch, Seed, id: Seed.CommandId).Get<CommitReceipt>());
        Outcome = RequiredProcessingPolicyAssertions.Outcome(db.Store, db.Partition, "root", Seed.CommandId);
        await PolicyAsync(1L, true);
    }
    internal async Task PolicyAsync(long epoch, bool allow)
    {
        var principal = new PrincipalRecord(QueryCapabilityVersionRequests.Reader, Harness.Partition.TenantId,
            allow ? [new(Harness.Partition.DatabaseId, QueryCapabilityVersionRequests.Collection, Capability.Query | Capability.DocumentsRead)] : [], [])
        {
            PolicyEpoch = epoch
        };
        await RequiredProcessingPolicyAssertions.EqualAsync(principal, Harness.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());
    }
    public void Dispose() => Harness.Dispose();
}
