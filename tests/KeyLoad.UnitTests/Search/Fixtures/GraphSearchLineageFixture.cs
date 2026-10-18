using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchLineageFixture(bool expansion, bool emptyLabels) : IDisposable
{
    internal TestDatabase Harness
    {
        get;
    } = new();
    internal DatabaseEngine Owner
    {
        get;
        set;
    } = null!;
    internal ZoneTreeStore Store
    {
        get;
        set;
    } = null!;
    internal bool Expansion => expansion;
    internal bool EmptyLabels => emptyLabels;
    internal CommandRequest Seed
    {
        get;
        private set;
    } = null!;
    internal CommitReceipt Receipt
    {
        get;
        private set;
    } = null!;
    internal byte[] Outcome
    {
        get;
        private set;
    } = [];
    internal PartitionRef Partition => Harness.Partition;
    internal GraphSearchRequest Request => GraphSearchLineageRecipe.Request(Partition, expansion,
        emptyLabels ? [] : [GraphSearchLineageProtocol.Label]);

    internal async Task InitializeAsync()
    {
        Store = Harness.Store;
        Owner = Harness.Database;
        Harness.Configure(GraphSearchLineageProtocol.Collection, ResourceKind.Collection);
        Harness.Configure(GraphSearchLineageProtocol.EarlyGraph, ResourceKind.Graph);
        Harness.Configure(GraphSearchLineageProtocol.LateGraph, ResourceKind.Graph,
            fields: [new(GraphSearchLineageProtocol.LabelPath, GraphSearchLineageProtocol.Classification,
                GraphSearchLineageProtocol.LabelRead, GraphSearchLineageProtocol.LabelUse)]);
        Seed = GraphSearchLineageRecipe.Seed(Partition);
        var position = Store.Position + 1L;
        Receipt = Owner.ApplyEmbedded(Operation(), TestContext.Current!.Execution.CancellationToken).Get<CommitReceipt>();
        await GraphSearchLineageValues.ReceiptAsync(Receipt, new(Seed.CommandId, new(Store.Identity.Incarnation, Partition.AtomicPartitionId, position), GraphSearchLineageRecipe.Receipts, DurabilityProfile.ProcessDurable, 1));
        Outcome = Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Partition, GraphSearchLineageProtocol.Root, Seed.CommandId)))
            ?? throw new InvalidOperationException(GraphSearchLineageProtocol.Missing);
        await PolicyAsync(GraphSearchLineageProtocol.InitialEpoch, false);
    }

    internal Command<DatabaseCommand> Operation() => Owner.CreateCommand(OperationKind.Batch, Seed.CommandId,
        GraphSearchLineageProtocol.Root, default, NativeSerialization.Serialize(Seed));

    internal async Task PolicyAsync(long epoch, bool allow)
    {
        var expected = new PrincipalRecord(GraphSearchLineageProtocol.Reader, Partition.TenantId,
            [new(Partition.DatabaseId, GraphSearchLineageProtocol.Collection, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch),
             new(Partition.DatabaseId, GraphSearchLineageProtocol.EarlyGraph, Capability.GraphRead),
             new(Partition.DatabaseId, GraphSearchLineageProtocol.LateGraph, Capability.GraphRead)], allow ? [GraphSearchLineageProtocol.LabelUse] : [])
        {
            PolicyEpoch = epoch
        };
        var operation = Owner.CreateCommand(OperationKind.ConfigurePrincipal, Guid.NewGuid(), GraphSearchLineageProtocol.Root,
            default, NativeSerialization.Serialize(new ConfigurePrincipalRequest(expected)));
        await GraphSearchLineageValues.EqualAsync(Owner.ApplyEmbedded(operation, TestContext.Current!.Execution.CancellationToken).Get<PrincipalRecord>(), expected);
    }
    public void Dispose() => Harness.Dispose();
}
