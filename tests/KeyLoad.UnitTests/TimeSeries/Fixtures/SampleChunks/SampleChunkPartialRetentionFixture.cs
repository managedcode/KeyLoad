using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkPartialRetentionFixture : IDisposable
{
    internal const string LateTags = "{\"visible\":18}";
    internal const long LateSequence = 3;
    internal const long FreshSequence = 4;
    internal const int FreshTickOffset = 2;
    internal const double FreshValue = 8;
    internal static readonly SampleData Fresh = new("partial-floor-fresh",
        SampleChunkCanonicalFixture.Start.AddTicks(FreshTickOffset), FreshValue);
    private readonly TestDatabase original;
    private readonly List<ZoneTreeStore> opened = [];
    internal SampleChunkRetentionClock Clock { get; } = new(SampleChunkCanonicalFixture.Start);
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
    internal List<(Command<DatabaseCommand> Operation, Result<object> Result)> Outcomes { get; } = [];

    internal SampleChunkPartialRetentionFixture()
    {
        original = new(timeProvider: Clock);
        Store = original.Store;
        Owner = original.Database;
        opened.Add(Store);
        try
        {
            SampleAggregateTestData.Configure(original);
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
        return Owner.CreateCommand(OperationKind.Batch, id, SampleChunkCanonicalFixture.Principal, Clock.GetUtcNow(),
            JsonSerializer.Serialize(new CommandRequest(id, Partition, [mutation]), JsonDefaults.Options));
    }

    internal Result<object> Apply(Command<DatabaseCommand> operation)
    {
        var result = Owner.Apply(operation);
        Outcomes.Add((operation, result));
        return result;
    }

    internal void Commit(Mutation mutation) => Apply(Operation(mutation)).Get<CommitReceipt>();

    internal SampleChunkWindowResult Read(CancellationToken token = default)
        => Owner.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal,
            new(Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                WindowId, null, null, SampleChunkCanonicalFixture.OutputLimit), token);

    internal void Reopen()
    {
        Store.Dispose();
        var next = new ZoneTreeStore(new(original.Directory), original.StorageExecution,
            UnitExecutionOptions.PointCacheExecution(), timeProvider: Clock);
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
            catch (Exception failure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
