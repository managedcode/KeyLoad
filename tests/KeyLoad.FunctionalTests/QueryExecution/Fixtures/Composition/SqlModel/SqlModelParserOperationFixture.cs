using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlModelParserOperationFixture : IDisposable
{
    private readonly TestDatabase original = new();
    private (Command<DatabaseCommand> Operation, CommitReceipt Receipt)? seed;
    private const long DeniedPolicyEpoch = 1;
    private const long RepairedPolicyEpoch = 2;
    private const string SeedRequired = "The original acknowledged model seed is required.";
    private const string HeaderPath = "/privateHeader";
    private const string PrivateTag = "private";
    private const string HeaderRead = "header.read";
    private const string HeaderUse = "header.use";
    private const string SecretRead = "secret.read";
    private const string SecretUse = "secret.use";
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
    internal Command<DatabaseCommand> Seed => seed?.Operation ?? throw new InvalidOperationException(SeedRequired);
    internal CommitReceipt Original => seed?.Receipt ?? throw new InvalidOperationException(SeedRequired);

    internal SqlModelParserOperationFixture()
    {
        Store = original.Store;
        Owner = original.Database;
    }

    internal void Initialize()
    {
        SqlModelViewTestSupport.Configure(original, SqlModelViewTestSupport.StreamSet, ResourceKind.StreamSet,
            headerPolicies: [new(HeaderPath, PrivateTag, HeaderRead, HeaderUse)]);
        SqlModelViewTestSupport.Configure(original, SqlModelViewTestSupport.Queue, ResourceKind.WorkQueue,
            headerPolicies: [new(HeaderPath, PrivateTag, HeaderRead, HeaderUse)]);
        var command = Guid.NewGuid();
        var payload = SqlModelViewTestSupport.Payload(SqlModelViewTestSupport.Secret, "event-visible");
        var headers = SqlModelViewTestSupport.Headers(SqlModelViewTestSupport.HeaderSecret);
        var request = new CommandRequest(command, Partition,
            [new AppendEvents(SqlModelViewTestSupport.StreamSet, SqlModelViewTestSupport.StreamId,
                [new(SqlModelViewTestSupport.EventId, "Created", payload, headers),
                 new(SqlModelParserOperationData.SecondEvent, "Updated", payload, headers)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(SqlModelViewTestSupport.Queue, SqlModelParserOperationData.SecondMessage,
                SqlModelViewTestSupport.Payload(SqlModelViewTestSupport.Secret, "queue-visible"), headers),
             new EnqueueMessage(SqlModelViewTestSupport.Queue, SqlModelViewTestSupport.MessageId,
                SqlModelViewTestSupport.Payload(SqlModelViewTestSupport.Secret, "queue-visible"), headers)]);
        var operation = Operation(OperationKind.Batch, request, command);
        var receipt = Owner.Apply(operation).Get<CommitReceipt>();
        seed = (operation, receipt);
    }

    internal PrincipalRecord Principal(bool repaired)
    {
        var record = new PrincipalRecord(SqlModelParserOperationData.Reader, Partition.TenantId,
            [new(Partition.DatabaseId, SqlModelViewTestSupport.StreamSet, Capability.Query | (repaired ? Capability.EventsRead : Capability.None)),
             new(Partition.DatabaseId, SqlModelViewTestSupport.Queue, Capability.Query | (repaired ? Capability.QueueInspect : Capability.None))], [SecretRead, SecretUse, HeaderRead, HeaderUse])
        {
            PolicyEpoch = repaired ? RepairedPolicyEpoch : DeniedPolicyEpoch
        };
        return Owner.Apply(Operation(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record), Guid.NewGuid()))
            .Get<PrincipalRecord>();
    }

    private Command<DatabaseCommand> Operation<T>(OperationKind kind, T payload, Guid command)
        => Owner.CreateCommand(kind, command, SqlModelParserOperationData.Root, Owner.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(payload, JsonDefaults.Options));

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
        foreach (var store in opened.AsEnumerable().Reverse())
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.Observe(original.Store.Dispose, failures);
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
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
            {
                failures.Add(error);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
