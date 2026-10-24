using System.Text.Json;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class ChunkSelectedIntegrityFixture : IDisposable
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
    internal Guid WindowId { get; } = Guid.NewGuid();
    internal List<(Command<DatabaseCommand> Operation, CommitReceipt Receipt)> Successes { get; } = [];
    internal Command<DatabaseCommand> Seal
    {
        get;
    }
    internal Command<DatabaseCommand> Merge
    {
        get;
    }
    internal const string OtherSeries = "selected-healthy";
    internal static readonly SampleData Fresh = new("fresh-correction", SampleChunkCanonicalFixture.Start.AddTicks(2), 8);
    internal static readonly SampleData Other = new("independent", SampleChunkCanonicalFixture.Start.AddMinutes(1), 12);

    internal ChunkSelectedIntegrityFixture()
    {
        Store = original.Store;
        Owner = original.Database;
        opened.Add(Store);
        try
        {
            SampleAggregateTestData.Configure(original);
            Apply(Operation(new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                WindowId, SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until)));
            Apply(Operation(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags)));
            Seal = Operation(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                WindowId, SampleChunkCanonicalFixture.AppendedRevision));
            Apply(Seal);
            Apply(Operation(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags)));
            Apply(Operation(new AppendSamples(SampleChunkCanonicalFixture.Set, OtherSeries, [Other], SampleChunkCanonicalFixture.Tags)));
            Merge = Operation(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                WindowId, SampleChunkCanonicalFixture.CorrectedRevision));
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
        return Owner.CreateCommand(OperationKind.Batch, id, SampleChunkCanonicalFixture.Principal, Owner.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(new CommandRequest(id, Partition, [mutation]), JsonDefaults.Options));
    }

    internal CommitReceipt Apply(Command<DatabaseCommand> operation)
    {
        var result = Owner.Apply(operation).Get<CommitReceipt>();
        Successes.Add((operation, result));
        return result;
    }

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
            catch (Exception error) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
