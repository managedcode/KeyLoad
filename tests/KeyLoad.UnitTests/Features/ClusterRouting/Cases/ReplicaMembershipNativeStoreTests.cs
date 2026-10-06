using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-001: native membership uses a genuine single-voter commit/apply cut and real-store CAS.</summary>
internal sealed class ReplicaMembershipNativeStoreTests
{
    private const int FirstVersion = 1;
    private const int SiloPort = 11_111;
    private const string ZeroEtag = "0";
    private const string Voter = "http://membership-native-unit:8080";
    private const string ClusterId = "membership-native-unit";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);
    private static readonly byte[] Key = KeyCodec.Encode(ReplicaMembershipProtocol.StorageSpace, ReplicaMembershipProtocol.TableKey);

    [Test]
    public async Task RealStorePersistsNativeSnapshotAndRejectsStaleCompareExchange()
    {
        await WithStoreAsync(async (fixture, store, log, token) =>
        {
            var initial = await store.ReadAsync(token);
            await Assert.That(fixture.Database.LastApplied).IsEqualTo(log.State.CommittedIndex);
            var initialCut = log.State.CommittedIndex;
            var inserted = initial.Insert(ReplicaMembershipNativeTests.Entry(), new TableVersion(FirstVersion, ZeroEtag))!;
            await Assert.That(await store.CompareExchangeAsync(inserted, token)).IsTrue();
            await Assert.That(await store.CompareExchangeAsync(inserted, token)).IsFalse();
            var persisted = fixture.Store.Read(view => view.GetRecord<MembershipRecord>(Key))!;
            var native = NativeSerialization.Deserialize<ReplicaMembershipTableSnapshot>(persisted.Payload.Span);
            await Assert.That(persisted.Version).IsEqualTo((long)FirstVersion);
            await Assert.That(native.Version).IsEqualTo((long)FirstVersion);
            await Assert.That(native.Rows.Single().Address).IsEqualTo(ReplicaMembershipNativeTests.Entry().SiloAddress.ToParsableString());
            var read = await store.ReadAsync(token);
            await Assert.That(read.ExpectedVersion).IsEqualTo(persisted.Version);
            await Assert.That(read.Data().Members.Single().Item1.SiloAddress).IsEqualTo(ReplicaMembershipNativeTests.Entry().SiloAddress);
            await Assert.That(fixture.Database.LastApplied).IsEqualTo(log.State.CommittedIndex);
            await Assert.That(log.State.CommittedIndex >= initialCut + 2).IsTrue();
        });
    }

    [Test]
    public async Task CancelledNativeCompareExchangeDoesNotCreateAnyMembershipRow()
    {
        await WithStoreAsync(async (fixture, store, log, token) =>
        {
            var initial = await store.ReadAsync(token);
            var inserted = initial.Insert(ReplicaMembershipNativeTests.Entry(), new TableVersion(FirstVersion, ZeroEtag))!;
            var before = log.State;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            await cancellation.CancelAsync();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.CompareExchangeAsync(inserted, cancellation.Token));
            await Assert.That(fixture.Store.Read(view => view.GetRecord<MembershipRecord>(Key))).IsNull();
            var read = store.ReadAsync(cancellation.Token);
            await Assert.That(read.IsCanceled).IsTrue();
            var cancelled = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => read);
            await Assert.That(cancelled).IsNotNull();
            await Assert.That(cancelled!.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(log.State).IsEqualTo(before);
            await Assert.That((await store.ReadAsync(token)).Data().Members.Count).IsEqualTo(0);
            await Assert.That(fixture.Database.LastApplied).IsEqualTo(log.State.CommittedIndex);
        });
    }

    private static async Task WithStoreAsync(Func<TestDatabase, ReplicaMembershipStore, DurableReplicaLog, CancellationToken, Task> verify)
    {
        using var fixture = new TestDatabase();
        ClusterPrincipalPolicy.Initialize(fixture.Database);
        var configuration = new ReplicaConfiguration(Voter, [Voter], Path.Combine(fixture.Directory, ReplicaProtocol.ReplicaDirectory),
            fixture.Store.Identity.Incarnation)
        { BenchmarkTopology = true };
        using var replica = new ZoneTreeStore(new(configuration.Directory)
        { Incarnation = configuration.Incarnation, SigningKey = fixture.Store.Identity.SigningKey }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        using var log = new DurableReplicaLog(replica, ReplicaExecutionTestOptions.Configuration(configuration), canonicalDatabase: fixture.Database);
        await using var materializer = new ReplicaMaterializer(fixture.Database, log, new ReplicaSnapshotStore(fixture.Store, log,
            ReplicaExecutionTestOptions.Configuration(configuration), ReplicaExecutionTestOptions.Execution()), ReplicaExecutionTestOptions.Execution());
        await using var consensus = new ReplicaConsensus(materializer, ReplicaExecutionTestOptions.Configuration(configuration),
            ReplicaExecutionTestOptions.Execution(), TimeProvider.System);
        await using var coordinator = new ClusterCoordinator(consensus, fixture.Database, new CommandAdmissionGovernor(UnitAdmissionOptions.Command()), TimeProvider.System,
            ReplicaExecutionTestOptions.Execution());
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        using var host = new HostBuilder().UseOrleans(silo =>
        {
            silo.Services.Configure<EndpointOptions>(endpoint =>
            { endpoint.AdvertisedIPAddress = IPAddress.Loopback; endpoint.SiloPort = SiloPort; endpoint.GatewayPort = 0; });
            silo.Services.AddSingleton<IMembershipTable>(new ReplicaMembershipTable(fixture.Database, coordinator,
                consensus, ClusterId, ClusterPrincipalPolicy.InternalPrincipalId, TimeProvider.System,
                UnitRoutingOptions.Membership(),
                ReplicaExecutionTestOptions.Execution(), linked.Token));
        }).Build();
        var configurationOptions = ReplicaExecutionTestOptions.Configuration(configuration);
        var discoveryOptions = UnitRoutingOptions.Discovery();
        var peers = new ReplicaPeerOptions(new() { [Voter] = new(Voter) }, fixture.Store.Identity.SigningKey, ClusterId)
        { ConnectTimeout = discoveryOptions.Value.ConnectTimeout };
        peers.Validate(configuration);
        var peerOptions = Options.Create(peers);
        var transportOptions = UnitRoutingOptions.Transport();
        var local = new ReplicaSiloDiscoveryState(configurationOptions, peerOptions, host.Services.GetRequiredService<ILocalSiloDetails>());
        using var authentication = new ReplicaEnvelopeAuthenticator(configurationOptions, peerOptions, local, TimeProvider.System,             transportOptions, UnitRoutingOptions.Replay(),             canonicalDatabase: fixture.Database);
        using var discovery = new ReplicaSiloDiscoveryClient(configurationOptions, peerOptions, local, authentication, TimeProvider.System,
            discoveryOptions);
        await coordinator.StartAsync(linked.Token);
        consensus.AttachTransport(new ReplicaGrainServiceClient(host.Services, configurationOptions, discovery, authentication));
        local.MarkTransportReady();
        await ReadyLeaderAsync(consensus, linked.Token);
        var store = new ReplicaMembershipStore(fixture.Database, coordinator, consensus, ClusterPrincipalPolicy.InternalPrincipalId);
        await verify(fixture, store, log, linked.Token);
    }

    private static async Task ReadyLeaderAsync(ReplicaConsensus consensus, CancellationToken cancellationToken)
    {
        while (!await consensus.IsLeaderAsync(cancellationToken))
        {
            await Task.Delay(PollInterval, TimeProvider.System, cancellationToken);
        }
    }
}
