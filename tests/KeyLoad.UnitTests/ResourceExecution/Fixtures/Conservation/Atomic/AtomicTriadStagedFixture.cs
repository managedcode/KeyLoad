using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class AtomicTriadStagedFixture : IDisposable
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
    internal PartitionRef Partition => Harness.Partition;
    internal CommandRequest Failed
    {
        get;
        private set;
    } = null!;
    internal CommandRequest Healthy
    {
        get;
        set;
    } = null!;
    internal CommitReceipt Receipt
    {
        get;
        set;
    } = null!;
    internal DateTimeOffset CommittedAt
    {
        get;
        set;
    }
    internal List<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Bytes)> Originals
    {
        get;
    } = [];
    internal bool HealthyCommitted
    {
        get;
        set;
    }
    internal List<(Command<DatabaseCommand> Operation, Result<object> Result)> Produced
    {
        get;
    } = [];
    internal void Initialize()
    {
        Owner = Harness.Database;
        Store = Harness.Store;
        Harness.Configure(AtomicTriadStagedProtocol.Collection, ResourceKind.Collection, indexes: [new(AtomicTriadStagedProtocol.Index, [AtomicTriadStagedProtocol.IndexField], true)]);
        Harness.Configure(AtomicTriadStagedProtocol.Streams, ResourceKind.StreamSet);
        Harness.Configure(AtomicTriadStagedProtocol.Queue, ResourceKind.WorkQueue);
        SetPolicy(AtomicTriadStagedProtocol.InitialEpoch, true);
        Failed = AtomicTriadStagedRecipe.Command(Partition, true);
    }
    internal Command<DatabaseCommand> Operation(CommandRequest command)
        => Owner.CreateCommand(OperationKind.Batch, command.CommandId, AtomicTriadStagedProtocol.Producer,
            default, OrleansSerialization.Serialize(command));
    internal PrincipalRecord SetPolicy(long epoch, bool publish)
    {
        var expected = AtomicTriadStagedRecipe.Principal(Partition, epoch, publish);
        var operation = Owner.CreateCommand(OperationKind.ConfigurePrincipal, Guid.NewGuid(), AtomicTriadStagedProtocol.Root,
            default, OrleansSerialization.Serialize(new ConfigurePrincipalRequest(expected)));
        return Owner.ApplyEmbedded(operation, TestContext.Current!.Execution.CancellationToken).Get<PrincipalRecord>();
    }
    public void Dispose() => Harness.Dispose();
}
