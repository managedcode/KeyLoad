using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.DocumentStorage;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaSubmissionOutcomeFlow : IDisposable
{
    internal const string Resource = "strict-documents";
    internal const string Writer = "strict-submission-writer";
    internal TestDatabase Database { get; } = new(nativeReplicaAdmission: true);
    internal PrincipalRecord Principal
    {
        get;
    }

    internal ReplicaSubmissionOutcomeFlow()
    {
        try
        {
            Database.Configure(Resource, ResourceKind.Collection);
            Principal = Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
                new(Writer, Database.Partition.TenantId, [new(Database.Partition.DatabaseId, Resource, Capability.All)], ["*"])))
                .Get<PrincipalRecord>();
        }
        catch (Exception primary)
        {
            try
            {
                Database.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    internal ReplicatedOperation Create(string document, string json, long expectedRevision = 0)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, Database.Partition,
            [new PutDocument(Resource, document, json, expectedRevision)]);
        return Database.Database.CreateNativeOperation(OperationKind.Batch, id, Writer,
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(command));
    }

    internal OperationResult Submit(ReplicatedOperation operation) => Database.SubmitIssued(operation);

    internal byte[] Document(string id) => Database.Store.Read(view =>
        view.ReadOwnedValue(DocumentStorageKeys.RecordKey(Database.Partition, Resource, id))
            ?? throw new InvalidOperationException("The genuine committed document is missing."));

    internal byte[] Outcome(ReplicatedOperation operation) => Database.Store.Read(view =>
    {
        var selected = CommandOutcomeKeyResolver.Select(view, operation.PrincipalId, operation.Id,
            CommandOutcomePartitionIdentity.Resolve(operation));
        return view.ReadOwnedValue(selected.Key)
            ?? throw new InvalidOperationException("The genuine stored command outcome is missing.");
    });

    internal static async Task RequireResultAsync(OperationResult expected, OperationResult actual)
    {
        await Assert.That(actual.Error).IsEqualTo(expected.Error);
        await Assert.That(actual.SafeDetail).IsEqualTo(expected.SafeDetail);
        await RequireBytesAsync(NativeSerialization.Serialize(expected), NativeSerialization.Serialize(actual));
    }

    internal static async Task RequireBytesAsync(byte[] expected, byte[] actual)
        => await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();

    internal static async Task RunAsync(Func<ReplicaSubmissionOutcomeFlow, Task> body)
    {
        var failures = new List<Exception>();
        try
        {
            using var flow = new ReplicaSubmissionOutcomeFlow();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => body(flow), failures);
        }
        catch (Exception error) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose() => Database.Dispose();
}
