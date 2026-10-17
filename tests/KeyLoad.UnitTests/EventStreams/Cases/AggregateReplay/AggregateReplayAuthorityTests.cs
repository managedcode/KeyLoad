using System.Text.Json;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateReplayAuthorityTests
{
    private const string NoReplayPrincipal = "aggregate-no-replay";
    private const string NoEventsPrincipal = "aggregate-no-events";
    private const string NoRawUsePrincipal = "aggregate-no-raw-use";
    private const string NoPayloadReadPrincipal = "aggregate-no-payload-read";
    private const string NoPayloadUsePrincipal = "aggregate-no-payload-use";
    private const string NoHeaderReadPrincipal = "aggregate-no-header-read";
    private const string NoHeaderUsePrincipal = "aggregate-no-header-use";
    private const string NoSnapshotManagePrincipal = "aggregate-no-snapshot-manage";
    private const string NoRawSnapshotPrincipal = "aggregate-no-raw-snapshot";
    private const string SecretField = "secret";
    private const string HeaderSecretField = "privateHeader";
    private const string SecretPayload = "aggregate-payload-secret";
    private const string SecretHeader = "aggregate-header-secret";
    private const string AuthEventId = "aggregate-auth-event";
    private const string PrivateEventId = "aggregate-private-event";
    private const string RetryCommandId = "baf8e703-3e22-4c39-b89f-ef3ee2a33f30";

    [Test]
    public async Task AcEvent009RequiresPersistedEventsReplayAndEventsReadCapabilities()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(new EventData(AuthEventId, AggregateReplayFixture.EventType, "{}"));
        fixture.ConfigurePrincipal(NoReplayPrincipal, Capability.EventsRead, []);
        fixture.ConfigurePrincipal(NoEventsPrincipal, Capability.EventsReplay, []);

        var replayDenied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(principal: NoReplayPrincipal));
        var eventReadDenied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(principal: NoEventsPrincipal));

        await Assert.That(replayDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(eventReadDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    [Test]
    public async Task AcEvent009SnapshotWritesRequirePersistedManagementAndRawInputGrants()
    {
        using var fixture = new AggregateReplayFixture(protectedFields: true);
        fixture.ConfigurePrincipal(NoSnapshotManagePrincipal, Capability.EventsRead,
            [AggregateReplayFixture.PayloadReadGrant, AggregateReplayFixture.PayloadUseGrant,
                AggregateReplayFixture.HeaderReadGrant, AggregateReplayFixture.HeaderUseGrant]);
        var permission = Assert.ThrowsExactly<KeyLoadException>(() => fixture.StoreSnapshot(0,
            principal: NoSnapshotManagePrincipal));
        await Assert.That(permission.Code).IsEqualTo(ErrorCode.PermissionDenied);

        fixture.ConfigurePrincipal(NoRawSnapshotPrincipal,
            Capability.EventsRead | Capability.EventsSnapshotsManage,
            [AggregateReplayFixture.PayloadReadGrant, AggregateReplayFixture.HeaderReadGrant,
                AggregateReplayFixture.HeaderUseGrant]);
        var rawInput = Assert.ThrowsExactly<KeyLoadException>(() => fixture.StoreSnapshot(0,
            principal: NoRawSnapshotPrincipal));
        await Assert.That(rawInput.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    [Test]
    public async Task AcEvent009RequiresRawReadAndUseEvenWhenPolicyIsNotRequiredForProcessing()
    {
        using var fixture = new AggregateReplayFixture(protectedFields: true);
        fixture.Append(new EventData(PrivateEventId, AggregateReplayFixture.EventType,
            Payload(SecretPayload), Headers(SecretHeader)));
        fixture.StoreSnapshot(1);
        fixture.ConfigurePrincipal(NoRawUsePrincipal,
            Capability.EventsReplay | Capability.EventsRead,
            [AggregateReplayFixture.PayloadReadGrant, AggregateReplayFixture.HeaderReadGrant,
                AggregateReplayFixture.HeaderUseGrant]);
        var snapshot = fixture.Snapshot();
        fixture.StoreEnvelope(new(99, snapshot));

        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(principal: NoRawUsePrincipal));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    [Test]
    public async Task AcEvent009EachMissingRawInputGrantDeniesBeforeReadingSnapshot()
    {
        using var fixture = ProtectedFixture();
        var grants = new[]
        {
            (NoPayloadReadPrincipal, new[] { AggregateReplayFixture.PayloadUseGrant,
                AggregateReplayFixture.HeaderReadGrant, AggregateReplayFixture.HeaderUseGrant }),
            (NoPayloadUsePrincipal, new[] { AggregateReplayFixture.PayloadReadGrant,
                AggregateReplayFixture.HeaderReadGrant, AggregateReplayFixture.HeaderUseGrant }),
            (NoHeaderReadPrincipal, new[] { AggregateReplayFixture.PayloadReadGrant,
                AggregateReplayFixture.PayloadUseGrant, AggregateReplayFixture.HeaderUseGrant }),
            (NoHeaderUsePrincipal, new[] { AggregateReplayFixture.PayloadReadGrant,
                AggregateReplayFixture.PayloadUseGrant, AggregateReplayFixture.HeaderReadGrant })
        };
        var snapshot = fixture.Snapshot();
        fixture.StoreEnvelope(new(99, snapshot));

        foreach (var (principal, fieldGrants) in grants)
        {
            fixture.ConfigurePrincipal(principal, Capability.EventsReplay | Capability.EventsRead, fieldGrants);
            var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(principal: principal));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
    }

    [Test]
    public async Task AcEvent009RevocationReauthorizesReplayAndDurableSnapshotRetry()
    {
        using var fixture = new AggregateReplayFixture(protectedFields: true);
        fixture.Append(new EventData(PrivateEventId, AggregateReplayFixture.EventType,
            Payload(SecretPayload), Headers(SecretHeader)));
        _ = fixture.Read(maximumEvents: 1);
        var originalReceipt = fixture.StoreSnapshot(1, commandId: Guid.Parse(RetryCommandId));
        var originalPage = fixture.Read(maximumEvents: 1);
        var originalEvent = fixture.Store.Read(view => view.ReadOwnedValue(fixture.EventKey(1)))!;
        var originalOutcome = fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(
            fixture.Partition, AggregateReplayFixture.WorkerId, Guid.Parse(RetryCommandId))))!;
        fixture.ConfigurePrincipal(AggregateReplayFixture.WorkerId,
            Capability.EventsReplay | Capability.EventsRead | Capability.EventsSnapshotsManage,
            [AggregateReplayFixture.PayloadReadGrant, AggregateReplayFixture.PayloadUseGrant,
                AggregateReplayFixture.HeaderReadGrant, AggregateReplayFixture.HeaderUseGrant], policyEpoch: 2,
            revoked: true);

        var read = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(maximumEvents: 1));
        var retry = Assert.ThrowsExactly<KeyLoadException>(() => fixture.StoreSnapshot(1,
            expectedVersion: 0, commandId: Guid.Parse(RetryCommandId)));

        await Assert.That(read.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(retry.Code).IsEqualTo(ErrorCode.Unauthenticated);
        foreach (var failure in new[] { read, retry })
        {
            await Assert.That(failure.Message.Contains(SecretPayload, StringComparison.Ordinal)).IsFalse();
            await Assert.That(failure.Message.Contains(SecretHeader, StringComparison.Ordinal)).IsFalse();
        }
        await AggregateReplayAuthorizationContinuation.RepairAndColdAsync(fixture,
            Guid.Parse(RetryCommandId), originalReceipt, originalPage, (originalEvent, originalOutcome));
    }

    [Test]
    public async Task AcEvent009AuthorizedWorkerReceivesExactRawSnapshotEventAndHeaders()
    {
        using var fixture = new AggregateReplayFixture(protectedFields: true);
        fixture.Append(new EventData(PrivateEventId, AggregateReplayFixture.EventType,
            Payload(SecretPayload), Headers(SecretHeader)));
        fixture.StoreSnapshot(0);

        var page = fixture.Read(maximumEvents: 1);
        var record = await Assert.That(page.Events).HasSingleItem();
        using var payload = JsonDocument.Parse(record.Data.PayloadJson);
        using var headers = JsonDocument.Parse(record.Data.HeadersJson);

        await Assert.That(page.Snapshot!.StateJson).IsEqualTo(AggregateReplayFixture.StateJson);
        await Assert.That(payload.RootElement.GetProperty(SecretField).GetString()).IsEqualTo(SecretPayload);
        await Assert.That(headers.RootElement.GetProperty(HeaderSecretField).GetString()).IsEqualTo(SecretHeader);
    }

    private static string Payload(string value) => "{\"" + SecretField + "\":\"" + value + "\"}";
    private static string Headers(string value) => "{\"" + HeaderSecretField + "\":\"" + value + "\"}";

    private static AggregateReplayFixture ProtectedFixture()
    {
        var fixture = new AggregateReplayFixture(protectedFields: true);
        fixture.Append(new EventData(PrivateEventId, AggregateReplayFixture.EventType,
            Payload(SecretPayload), Headers(SecretHeader)));
        fixture.StoreSnapshot(0);
        return fixture;
    }
}
