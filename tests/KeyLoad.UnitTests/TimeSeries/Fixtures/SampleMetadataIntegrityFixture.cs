using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleMetadataIntegrityFixture : IDisposable
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
    internal ReplicatedOperation SeedOperation
    {
        get;
    }
    internal CommitReceipt SeedReceipt
    {
        get;
    }
    internal ReplicatedOperation BucketOperation
    {
        get;
    }
    internal CommitReceipt BucketReceipt
    {
        get;
    }

    internal SampleMetadataIntegrityFixture()
    {
        Store = original.Store;
        Owner = original.Database;
        opened.Add(Store);
        try
        {
            SampleAggregateTestData.Configure(original);
            SeedOperation = Operation(new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                [SampleMetadataIntegrityScenario.Sample(1), SampleMetadataIntegrityScenario.Sample(2),
                    SampleMetadataIntegrityScenario.Sample(3), SampleMetadataIntegrityScenario.Sample(4)],
                SampleAggregateTestData.EmptyTags));
            SeedReceipt = Apply(SeedOperation);
            Apply(Operation(new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleMetadataIntegrityScenario.Floor, 1)));
            BucketOperation = Operation(SampleMetadataIntegrityScenario.Refresh(0));
            BucketReceipt = Apply(BucketOperation);
        }
        catch (Exception initiating)
        {
            var failures = new List<Exception> { initiating };
            KeyLoad.Server.ServerFailureObserver.Observe(Dispose, failures);
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal ReplicatedOperation Operation(Mutation mutation)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, Partition, [mutation]);
        return new(id, OperationKind.Batch, SampleAggregateTestData.RootPrincipal,
            Owner.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options));
    }

    internal CommitReceipt Apply(ReplicatedOperation operation) => Owner.Apply(operation).Get<CommitReceipt>();

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
