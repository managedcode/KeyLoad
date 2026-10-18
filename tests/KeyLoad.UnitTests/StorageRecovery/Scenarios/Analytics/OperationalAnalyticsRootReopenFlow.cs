using System.Security.Cryptography;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Authorization;
using KeyLoad.UnitTests.Features.Messaging;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class OperationalAnalyticsRootReopenFlow
{
    private const string Tenant = "analytics-layout-tenant";
    private const string Database = "analytics-layout-database";
    private const string Domain = "orders";
    private const string PartitionKey = "one";
    private const string Collection = "analytics-layout-documents";
    private const string Document = "one";
    private const string InitialJson = "{\"value\":1}";
    private const string UpdatedJson = "{\"value\":2}";
    private const string PutKind = "putDocument";
    private static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);

    internal static Task RunAsync(CancellationToken token)
        => PartitionHostRecoveryFixture.RunAsync(async fixture =>
        {
            var cut = await SeedAsync(fixture, token);
            await fixture.RunHostAsync(host => ReopenAsync(fixture, host, cut, token));
        });

    private static async Task<Cut> SeedAsync(PartitionHostRecoveryFixture fixture, CancellationToken token)
    {
        Cut? cut = null;
        await fixture.RunHostAsync(async host =>
        {
            await BootstrapAsync(fixture, host, token);
            var definition = new ResourceDefinition(Collection, ResourceKind.Collection, Domain);
            var configure = host.Database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(),
                PartitionStoreProtocol.AdministratorId, TimeProvider.System.GetUtcNow(),
                NativeSerialization.Serialize(new ConfigureResourceRequest(Tenant, Database, definition)));
            await SafeProjectorOperationSeed.EqualAsync(host.Database.ApplyEmbedded(configure, token)
                .Get<ResourceDefinition>(), definition);
            var command = new CommandRequest(Guid.NewGuid(), Partition,
                [new PutDocument(Collection, Document, InitialJson, 0)]);
            var original = host.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
                PartitionStoreProtocol.AdministratorId, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(command));
            var receipt = await ApplyAsync(host, original, 1);
            cut = Capture(host, original, receipt);
            await AnalyticsAsync(fixture, host, token);
            await RequireCutAsync(host, cut);
        });
        return cut!;
    }

    private static async Task BootstrapAsync(PartitionHostRecoveryFixture fixture, PartitionHost host,
        CancellationToken token)
    {
        var request = new BootstrapPhysicalShardCatalogRequest(PhysicalShardCatalogStartupProtocol.Version,
            PhysicalShardCatalogStartupProtocol.ExpectedRevision, fixture.Options.PhysicalShardId,
            host.Configuration.Incarnation, host.Configuration.VoterIds);
        var operation = host.Database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
            PhysicalShardCatalogIdentity.CreateBootstrapCommandId(fixture.Options.PhysicalShardId),
            PartitionStoreProtocol.AdministratorId, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(request));
        await Assert.That(host.Database.ApplyEmbedded(operation, token).Get<bool>()).IsTrue();
        await SafeProjectorOperationSeed.EqualAsync(
            host.Database.ReadPhysicalShardCatalog(PartitionStoreProtocol.AdministratorId),
            new PhysicalShardCatalog(PhysicalShardCatalogStartupProtocol.Version,
                PhysicalShardCatalogStartupProtocol.InitialRevision, new(fixture.Options.PhysicalShardId,
                    host.Configuration.Incarnation, host.Configuration.VoterIds,
                    PhysicalShardCatalogStartupProtocol.InitialPlacementEpoch)));
        await SafeProjectorOperationSeed.EqualAsync(host.Database.ReadAtomicPartitionPlacement(
            PartitionStoreProtocol.AdministratorId, Partition),
            new AtomicPartitionPlacementResolution(PhysicalShardCatalogStartupProtocol.Version, Partition,
                fixture.Options.PhysicalShardId, host.Configuration.Incarnation, host.Configuration.VoterIds,
                PhysicalShardCatalogStartupProtocol.InitialPlacementEpoch,
                PhysicalShardCatalogStartupProtocol.ExpectedRevision,
                PhysicalShardCatalogStartupProtocol.ExpectedRevision, true));
    }

    private static async Task AnalyticsAsync(PartitionHostRecoveryFixture fixture, PartitionHost host,
        CancellationToken token)
    {
        OperationalAnalyticsOwner? original = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var analytics = original = new OperationalAnalyticsOwner(fixture.Options.DataDirectory,
                    Options.Create(new OperationalAnalyticsOptions()), TimeProvider.System);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var from = Hour(TimeProvider.System.GetUtcNow());
                    await Assert.That(analytics.TryRecord(await host.Consensus.StateAsync(token))).IsTrue();
                    await analytics.FlushAsync(token);
                    var report = await analytics.ReadAsync(from, Hour(TimeProvider.System.GetUtcNow()).AddHours(1), token);
                    await Assert.That(report.NativePlanComplete).IsTrue();
                    await Assert.That(report.Rows.Sum(row => row.Samples)).IsEqualTo(1d);
                    await Assert.That(report.Counters.Errors).IsEqualTo(0L);
                    await Assert.That(Directory.Exists(Path.Combine(fixture.Options.DataDirectory,
                        OperationalAnalyticsProtocol.DirectoryName))).IsTrue();
                }, failures);
            }, failures);
        }
        finally
        {
            if (original is not { HasJoinedEngine: true })
            {
                fixture.RetainRoot();
            }
        }
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(original is { HasJoinedEngine: true }).IsTrue(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ReopenAsync(PartitionHostRecoveryFixture fixture, PartitionHost host,
        Cut cut, CancellationToken token)
    {
        await RequireCutAsync(host, cut);
        await Assert.That(CryptographicOperations.FixedTimeEquals(host.Database.Store.Identity.SigningKey.Span,
            Convert.FromBase64String(fixture.Options.SigningKey))).IsTrue();
        await SafeProjectorOperationSeed.EqualAsync(host.Database.GetDocument(PartitionStoreProtocol.AdministratorId,
            new(Partition, Collection, Document), cancellationToken: token), new DocumentResult(new(Partition, Collection, Document), 1, InitialJson, false, []));
        await SafeProjectorOperationSeed.EqualAsync(host.Database.Apply(cut.Original).Get<CommitReceipt>(), cut.Receipt);
        await RequireCutAsync(host, cut);
        var command = new CommandRequest(Guid.NewGuid(), Partition,
            [new PutDocument(Collection, Document, UpdatedJson, 1)]);
        var original = host.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId,
            PartitionStoreProtocol.AdministratorId, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(command));
        var receipt = await ApplyAsync(host, original, 2);
        var updated = Capture(host, original, receipt);
        await SafeProjectorOperationSeed.EqualAsync(host.Database.GetDocument(PartitionStoreProtocol.AdministratorId,
            new(Partition, Collection, Document), cancellationToken: token), new DocumentResult(new(Partition, Collection, Document), 2, UpdatedJson, false, []));
        await SafeProjectorOperationSeed.EqualAsync(host.Database.Apply(original).Get<CommitReceipt>(), receipt);
        await RequireCutAsync(host, updated);
        token.ThrowIfCancellationRequested();
    }

    private static async Task<CommitReceipt> ApplyAsync(PartitionHost host, ReplicatedOperation original, long revision)
    {
        var expected = new CommitReceipt(original.Id,
            new(host.Database.Store.Identity.Incarnation, Partition.AtomicPartitionId,
                checked(host.Database.Store.Position + 1), 1),
            [new(PutKind, Collection, Document, revision)], DurabilityProfile.QuorumProcessDurable);
        var actual = host.Database.Apply(original).Get<CommitReceipt>();
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await SafeProjectorOperationSeed.EqualAsync(actual, expected);
        return actual;
    }

    private static Cut Capture(PartitionHost host, ReplicatedOperation original, CommitReceipt receipt)
        => new(original, receipt, host.Database.Store.Identity, host.Database.Store.Position,
            QueueWholeFlowStorage.Bytes((ZoneTreeStore)host.Database.Store));

    private static async Task RequireCutAsync(PartitionHost host, Cut cut)
    {
        var identity = host.Database.Store.Identity;
        await Assert.That(identity.NodeId).IsEqualTo(cut.Identity.NodeId);
        await Assert.That(identity.Incarnation).IsEqualTo(cut.Identity.Incarnation);
        await Assert.That(identity.FormatVersion).IsEqualTo(cut.Identity.FormatVersion);
        await Assert.That(identity.KeyCodecVersion).IsEqualTo(cut.Identity.KeyCodecVersion);
        await Assert.That(identity.MinimumReaderContract).IsEqualTo(cut.Identity.MinimumReaderContract);
        await Assert.That(identity.ReadGeneration).IsGreaterThanOrEqualTo(cut.Identity.ReadGeneration);
        await Assert.That(identity.Durability).IsEqualTo(cut.Identity.Durability);
        await Assert.That(identity.DispatchPaused).IsEqualTo(cut.Identity.DispatchPaused);
        await Assert.That(host.Database.Store.Position).IsEqualTo(cut.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes((ZoneTreeStore)host.Database.Store)
            .SequenceEqual(cut.Rows, StringComparer.Ordinal)).IsTrue();
    }

    private static DateTimeOffset Hour(DateTimeOffset value)
        => new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero);

    private sealed record Cut(ReplicatedOperation Original, CommitReceipt Receipt, StoreIdentity Identity,
        long Position, string[] Rows);
}
