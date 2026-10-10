using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupPrivacy
{
    private const string Inspector = "mixed-current-inspector";
    private const string Redacted = "{}";

    internal static async Task RunAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        var worker = new PrincipalRecord(Inspector, state.Queue.Partition.TenantId,
            [new(state.Queue.Partition.DatabaseId, state.Queue.Lane.Queue,
                Capability.QueueInspect | Capability.DeadLettersRead)], []);
        Configure(fixture.Database, worker, token);
        await RequireAsync(fixture.Database, state);
        var original = MixedEventingRetentionBackupOracle.Rows(fixture.Target!, state.Queue.Partition, includeOutcomes: false);
        Configure(fixture.Database, worker with { PolicyEpoch = 2, Revoked = true }, token);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.InspectMessage(Inspector,
            state.Queue.Lane, QueueLifecycleTestProtocol.Pending));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(MixedEventingRetentionBackupOracle.Rows(fixture.Target!, state.Queue.Partition, includeOutcomes: false))
            .IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        Configure(fixture.Database, worker with { PolicyEpoch = 3 }, token);
        await RequireAsync(fixture.Database, state);
    }

    private static void Configure(DatabaseEngine database, PrincipalRecord principal, CancellationToken token)
        => database.ApplyEmbedded(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal, EventingArtifactFixture.Principal,
            default, System.Text.Json.JsonSerializer.Serialize(new ConfigurePrincipalRequest(principal), JsonDefaults.Options)), token)
            .Get<PrincipalRecord>();

    internal static async Task RequireAsync(DatabaseEngine database, MixedEventingRetentionBackupState state)
    {
        foreach (var id in new[] { QueueLifecycleTestProtocol.Pending, QueueLifecycleTestProtocol.Parked })
        {
            var expected = database.InspectMessage(EventingArtifactFixture.Principal, state.Queue.Lane, id)!;
            var actual = database.InspectMessage(Inspector, state.Queue.Lane, id)!;
            await Assert.That(actual.Metadata).IsEqualTo(expected.Metadata);
            await Assert.That(actual.PayloadJson).IsEqualTo(expected.PayloadJson is null ? null : Redacted);
            await Assert.That(actual.HeadersJson).IsEqualTo(expected.HeadersJson);
        }
    }
}
