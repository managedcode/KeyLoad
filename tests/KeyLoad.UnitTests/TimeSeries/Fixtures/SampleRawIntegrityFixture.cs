using System.Text.Json;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class SampleRawIntegrityFixture : IDisposable
{
    private readonly TestDatabase original = new();
    private readonly List<ZoneTreeStore> opened = [];
    internal ZoneTreeStore Store
    {
        get; private set;
    }
    internal DatabaseEngine Owner
    {
        get; private set;
    }
    internal PartitionRef Partition => original.Partition;
    internal Command<DatabaseCommand> SeedOperation
    {
        get;
    }
    internal CommitReceipt SeedReceipt
    {
        get;
    }

    internal SampleRawIntegrityFixture()
    {
        Store = original.Store;
        Owner = original.Database;
        opened.Add(Store);
        try
        {
            SampleAggregateTestData.Configure(original);
            SeedOperation = Operation(new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                [SampleRawIntegrityScenario.Sample(1), SampleRawIntegrityScenario.Sample(2),
                    SampleRawIntegrityScenario.Sample(3), SampleRawIntegrityScenario.Sample(4)],
                SampleAggregateTestData.EmptyTags));
            SeedReceipt = Apply(SeedOperation);
            Apply(Operation(new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleRawIntegrityScenario.InitialFloor, 1)));
        }
        catch (Exception initiating)
        {
            var failures = new List<Exception> { initiating };
            KeyLoad.Server.ServerFailureObserver.Observe(Dispose, failures);
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Command<DatabaseCommand> Operation(Mutation mutation)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, Partition, [mutation]);
        return Owner.CreateCommand(OperationKind.Batch, id, SampleAggregateTestData.RootPrincipal,
            Owner.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options));
    }

    internal CommitReceipt Apply(Command<DatabaseCommand> operation) => Owner.Apply(operation).Get<CommitReceipt>();

    internal void Reopen()
    {
        Store.Dispose();
        var next = new ZoneTreeStore(new(original.Directory), original.StorageExecution,
            UnitExecutionOptions.PointCacheExecution(), timeProvider: Owner.EvaluationClock);
        opened.Add(next);
        Store = next;
        Owner = original.CreateConfiguredDatabase(next);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        for (var index = opened.Count - 1; index >= 0; index--)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(opened[index].Dispose, failures);
        }
        if (failures.Count == 0)
        {
            try
            {
                original.Dispose();
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
            {
                failures.Add(error);
            }
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
