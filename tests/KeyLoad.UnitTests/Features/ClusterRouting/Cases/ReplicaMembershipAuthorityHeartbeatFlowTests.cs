using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Actual signed authority HTTP and native membership provider persist the native minimal heartbeat.</summary>
internal sealed class ReplicaMembershipAuthorityHeartbeatFlowTests
{
    private const int FirstVersion = 1;
    private const int NextGeneration = 1;
    private const string InitialEtag = "0";
    private static readonly byte[] Key = KeyCodec.Encode(ReplicaMembershipProtocol.StorageSpace, ReplicaMembershipProtocol.TableKey);

    [Test]
    public async Task NativeSignedHeartbeatPreservesFullRowRejectsWrongCallerThenHealthyContinuation()
    {
        await ReplicaMembershipNativeStoreTests.WithStoreAsync(async (fixture, store, table, log, token) =>
        {
            await ReplicaMembershipAuthorityHeartbeatHttp.WithAsync(fixture, table, async (client, options) =>
            {
                var original = ReplicaMembershipNativeTests.Entry();
                await Assert.That(await client.InsertRowAsync(original, new TableVersion(FirstVersion, InitialEtag), token)).IsTrue();
                var before = await store.ReadAsync(token);
                var heartbeat = new MembershipEntry
                { SiloAddress = original.SiloAddress, IAmAliveTime = original.IAmAliveTime.AddMinutes(FirstVersion) };
                await client.UpdateIAmAliveAsync(heartbeat, token);
                await ExactSnapshotAsync(fixture, before.Heartbeat(heartbeat)!);
                var after = await store.ReadAsync(token);
                await Assert.That(after.Data().Version.Version).IsEqualTo(before.Data().Version.Version);
                await Assert.That(after.Data().Version.VersionEtag).IsEqualTo(before.Data().Version.VersionEtag);
                await Assert.That(after.Data().Members.Single().Item2).IsEqualTo(before.Data().Members.Single().Item2);
                await Assert.That(after.Data().Members.Single().Item1.IAmAliveTime).IsEqualTo(heartbeat.IAmAliveTime);
                await NoEffectAsync(fixture, () => client.UpdateIAmAliveAsync(heartbeat, token));
                await NoEffectAsync(fixture, () => client.UpdateIAmAliveAsync(
                    new() { SiloAddress = original.SiloAddress, IAmAliveTime = original.IAmAliveTime }, token));
                var unknown = new MembershipEntry
                {
                    SiloAddress = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, original.SiloAddress.Endpoint.Port),
                        original.SiloAddress.Generation + NextGeneration),
                    IAmAliveTime = heartbeat.IAmAliveTime
                };
                await using (var other = ReplicaMembershipAuthorityHeartbeatHttp.Client(options, unknown.SiloAddress))
                { await NoEffectAsync(fixture, () => other.UpdateIAmAliveAsync(unknown, token)); }
                await NoEffectAsync(fixture, async () =>
                {
                    var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => client.UpdateIAmAliveAsync(unknown, token));
                    await Assert.That(error!.Code).IsEqualTo(ErrorCode.Unauthenticated);
                });
                await MalformedRegistrationAsync(fixture, client, heartbeat, token);
                heartbeat.IAmAliveTime = heartbeat.IAmAliveTime.AddMinutes(FirstVersion);
                await client.UpdateIAmAliveAsync(heartbeat, token);
                await ExactSnapshotAsync(fixture, after.Heartbeat(heartbeat)!);
                await Assert.That((await client.ReadRowAsync(original.SiloAddress, token)).Members.Single().Item1.IAmAliveTime)
                    .IsEqualTo(heartbeat.IAmAliveTime);
                await Assert.That(fixture.Database.LastApplied).IsEqualTo(log.State.CommittedIndex);
            }, token);
        });
    }

    private static async Task MalformedRegistrationAsync(TestDatabase fixture, ReplicaMembershipAuthorityClientTable client,
        MembershipEntry minimal, CancellationToken cancellationToken)
    {
        await NoEffectAsync(fixture, async () =>
        {
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => client.InsertRowAsync(minimal,
                new TableVersion(FirstVersion, InitialEtag), cancellationToken));
            await Assert.That(error!.Code).IsEqualTo(ErrorCode.Validation);
        });
        await NoEffectAsync(fixture, async () =>
        {
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => client.UpdateRowAsync(minimal,
                InitialEtag, new TableVersion(FirstVersion, InitialEtag), cancellationToken));
            await Assert.That(error!.Code).IsEqualTo(ErrorCode.Validation);
        });
    }

    private static async Task NoEffectAsync(TestDatabase fixture, Func<Task> operation)
    {
        var before = fixture.Store.Read(view => view.GetRecord<MembershipRecord>(Key))!;
        await operation();
        var after = fixture.Store.Read(view => view.GetRecord<MembershipRecord>(Key))!;
        await Assert.That(after.Version).IsEqualTo(before.Version);
        await Assert.That(after.Payload.Span.SequenceEqual(before.Payload.Span)).IsTrue();
    }

    private static async Task ExactSnapshotAsync(TestDatabase fixture, ReplicaMembershipSnapshot expected)
        => await Assert.That(fixture.Store.Read(view => view.GetRecord<MembershipRecord>(Key))!.Payload.Span
            .SequenceEqual(expected.Serialize())).IsTrue();
}
