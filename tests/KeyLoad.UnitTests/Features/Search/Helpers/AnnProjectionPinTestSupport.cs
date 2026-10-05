using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProjectionPinTestSupport
{
    internal const string Collection = AnnSeedTestSupport.Collection;
    internal const string Field = AnnSeedTestSupport.Field;
    internal const string Principal = "root";
    internal const string VectorMutation = "putVector";
    private const string ConsumerSpace = "projection-consumer";
    private const string OutboxSpace = "outbox";
    internal const string VectorId = "seed-00000";

    internal static TestDatabase Create(DatabaseLimits? limits = null)
        => AnnSeedTestSupport.Create(1, limits);

    internal static ProjectionConsumerRef Consumer(TestDatabase database, string name)
        => new(database.Partition, name);

    internal static long Tail(TestDatabase database)
        => database.Database.GetOutboxStatus(Principal, database.Partition).Head.Tail;

    internal static ProjectionConsumerInfo Configure(TestDatabase database, ProjectionConsumerRef consumer,
        long startAfter, string principal = Principal, long generation = 1)
    {
        var commandId = Guid.NewGuid();
        var definition = new ProjectionConsumerDefinition(generation, [Collection], [VectorMutation]);
        return database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(commandId, consumer, definition, startAfter),
            principal, commandId).Get<ProjectionConsumerInfo>();
    }

    internal static void PutVector(TestDatabase database, ImmutableArray<float> values)
        => AnnSeedTestSupport.CommitVector(database, VectorId, values, AnnSeedTestSupport.Space(), revision: 1);

    internal static AnnSeed Capture(TestDatabase database)
        => AnnSeedTestSupport.Capture(database, Principal);

    internal static (OutboxHead Head, long Position, byte[] AtTail, byte[] AfterTail)
        CaptureBridge(TestDatabase database, ProjectionConsumerRef consumer, long seedTail)
        => (database.Database.GetOutboxStatus(Principal, consumer.Partition).Head,
            database.Store.Position, StoredOutbox(database, seedTail), StoredOutbox(database, seedTail + 1));

    internal static ProjectionBatch Read(TestDatabase database, ProjectionConsumerRef consumer,
        string principal = Principal, int limit = 1, int maxBytes = 4_194_304)
        => database.Database.ReadProjectionBatch(principal, new(consumer, limit, maxBytes));

    internal static OperationResult CommitEmpty(TestDatabase database, ProjectionConsumerRef consumer,
        ProjectionBatch batch, string principal, Guid commandId, DateTimeOffset evaluatedAt)
    {
        var request = new CommitProjectionBatchRequest(commandId, consumer, batch.Token, []);
        return database.Submit(OperationKind.CommitProjectionBatch, request, principal, commandId, evaluatedAt);
    }

    internal static OperationResult Release(TestDatabase database, ProjectionConsumerRef consumer,
        string principal = Principal, long generation = 1)
    {
        var commandId = Guid.NewGuid();
        return database.Submit(OperationKind.ReleaseProjectionConsumer,
            new ReleaseProjectionConsumerRequest(commandId, consumer, generation), principal, commandId);
    }

    internal static OperationResult Purge(TestDatabase database, long through, string principal = Principal, int limit = 1_000)
    {
        var commandId = Guid.NewGuid();
        return database.Submit(OperationKind.PurgeOutbox,
            new PurgeOutboxRequest(commandId, database.Partition, through, limit), principal, commandId);
    }

    internal static byte[] StoredOutbox(TestDatabase database, long sequence)
        => database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Partition(OutboxSpace, database.Partition, sequence))!);

    internal static byte[] StoredConsumer(TestDatabase database, ProjectionConsumerRef consumer)
        => database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Partition(ConsumerSpace, consumer.Partition, consumer.Name))!);

    internal static bool ConsumerExists(TestDatabase database, ProjectionConsumerRef consumer)
        => database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Partition(ConsumerSpace, consumer.Partition, consumer.Name)) is not null);

    internal static void ConfigurePrincipal(TestDatabase database, PrincipalRecord principal)
        => database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();

    internal static PrincipalRecord Administrator(string id, string tenant, bool revoked = false, long epoch = 1)
        => new(id, tenant, [], []) { ClusterAdministrator = true, Revoked = revoked, PolicyEpoch = epoch };

    internal static PrincipalRecord OrdinaryPrincipal(string id, string tenant)
        => new(id, tenant, [], []);

    internal static ErrorCode ReadFailure(TestDatabase database, ProjectionConsumerRef consumer, string principal)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Read(database, consumer, principal));
        return failure.Code;
    }

    internal static ImmutableArray<byte[]> CaptureEntryBytes(TestDatabase database, long first, long last)
    {
        var bytes = ImmutableArray.CreateBuilder<byte[]>(checked((int)(last - first + 1)));
        for (var sequence = first; sequence <= last; sequence++)
        {
            bytes.Add(StoredOutbox(database, sequence));
        }
        return bytes.MoveToImmutable();
    }
}
