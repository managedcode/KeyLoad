namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnProjectionPinAuthorityTests
{
    private const string ConsumerName = "ann-pin-authority-v1";
    private const string AdminId = "ann-pin-admin";
    private const string OrdinaryId = "ann-pin-user";

    [Test]
    public async Task OnlyCurrentPersistedAdministratorsCanOperateThePin()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var activeAdmin = AnnProjectionPinTestSupport.Administrator(AdminId, database.Partition.TenantId);
        AnnProjectionPinTestSupport.ConfigurePrincipal(database, activeAdmin);
        AnnProjectionPinTestSupport.ConfigurePrincipal(database,
            AnnProjectionPinTestSupport.OrdinaryPrincipal(OrdinaryId, database.Partition.TenantId));

        var consumer = AnnProjectionPinTestSupport.Consumer(database, ConsumerName);
        var start = AnnProjectionPinTestSupport.Tail(database);
        AnnProjectionPinTestSupport.Configure(database, consumer, start, AdminId);
        AnnProjectionPinTestSupport.PutVector(database, [3.25f, 4.5f, 5.75f]);
        var batch = AnnProjectionPinTestSupport.Read(database, consumer, AdminId);
        var baselineHead = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head;
        var baselineConsumer = AnnProjectionPinTestSupport.StoredConsumer(database, consumer);
        var baselineEntry = AnnProjectionPinTestSupport.StoredOutbox(database, start + 1);

        await AssertRejectedOperationsAsync(database, consumer, batch, OrdinaryId, ErrorCode.PermissionDenied);

        AnnProjectionPinTestSupport.ConfigurePrincipal(database, activeAdmin with
        { Revoked = true, PolicyEpoch = activeAdmin.PolicyEpoch + 1 });
        await AssertRejectedOperationsAsync(database, consumer, batch, AdminId, ErrorCode.Unauthenticated);

        var after = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition);
        await Assert.That(after.Head).IsEqualTo(baselineHead);
        await Assert.That(after.Consumers.Single().Checkpoint).IsEqualTo(start);
        await Assert.That(AnnProjectionPinTestSupport.StoredConsumer(database, consumer)
            .SequenceEqual(baselineConsumer)).IsTrue();
        await Assert.That(AnnProjectionPinTestSupport.StoredOutbox(database, start + 1)
            .SequenceEqual(baselineEntry)).IsTrue();

        var cleanup = AnnProjectionPinTestSupport.Release(database, consumer);
        await Assert.That(cleanup.Error).IsNull();
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition)
            .Consumers.Single().Released).IsTrue();
    }

    private static async Task AssertRejectedOperationsAsync(TestDatabase database,
        ProjectionConsumerRef consumer, ProjectionBatch batch, string principal, ErrorCode expected)
    {
        var attemptedConsumer = AnnProjectionPinTestSupport.Consumer(database, principal + "-attempt");
        var configureId = Guid.NewGuid();
        var configure = database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(configureId, attemptedConsumer,
                new(1, [AnnProjectionPinTestSupport.Collection], [AnnProjectionPinTestSupport.VectorMutation]), 0), principal, configureId);
        await Assert.That(configure.Error).IsEqualTo(expected);
        await Assert.That(AnnProjectionPinTestSupport.ReadFailure(database, consumer, principal)).IsEqualTo(expected);

        var commit = AnnProjectionPinTestSupport.CommitEmpty(database, consumer, batch,
            principal, Guid.NewGuid(), TimeProvider.System.GetUtcNow());
        await Assert.That(commit.Error).IsEqualTo(expected);

        var release = AnnProjectionPinTestSupport.Release(database, consumer, principal);
        await Assert.That(release.Error).IsEqualTo(expected);

        var purge = AnnProjectionPinTestSupport.Purge(database,
            database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head.Tail, principal);
        await Assert.That(purge.Error).IsEqualTo(expected);
    }
}
