using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed record AggregateReplayRf3Scenario(PartitionRef Partition, PrincipalRecord Worker, string WorkerSecret)
{
    internal StreamRef Stream => new(Partition, AggregateReplayRf3Tokens.StreamSet,
        AggregateReplayRf3Tokens.StreamId, AggregateReplayRf3Tokens.Generation);

    internal static async Task<AggregateReplayRf3Scenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(AggregateReplayRf3Tokens.TenantPrefix
            + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat), AggregateReplayRf3Tokens.Database,
            AggregateReplayRf3Tokens.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var resource = new ResourceDefinition(AggregateReplayRf3Tokens.StreamSet,
            ResourceKind.StreamSet, partition.TransactionDomainId)
        {
            FieldPolicies = [new(AggregateReplayRf3Tokens.PayloadSecretPath,
                AggregateReplayRf3Tokens.PayloadClassification, AggregateReplayRf3Tokens.PayloadReadGrant,
                AggregateReplayRf3Tokens.PayloadUseGrant, AggregateReplayRf3Tokens.PayloadWriteGrant,
                RequiredForProcessing: false)],
            HeaderPolicies = [new(AggregateReplayRf3Tokens.HeaderSecretPath,
                AggregateReplayRf3Tokens.HeaderClassification, AggregateReplayRf3Tokens.HeaderReadGrant,
                AggregateReplayRf3Tokens.HeaderUseGrant, AggregateReplayRf3Tokens.HeaderWriteGrant,
                RequiredForProcessing: false)]
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), cancellationToken));
        var principalId = AggregateReplayRf3Tokens.WorkerIdPrefix
            + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var capabilities = Capability.EventsAppend | Capability.EventsRead | Capability.EventsReplay
            | Capability.EventsSnapshotsManage;
        var worker = new PrincipalRecord(principalId, partition.TenantId,
            [new(partition.DatabaseId, AggregateReplayRf3Tokens.StreamSet, capabilities)],
            [AggregateReplayRf3Tokens.PayloadReadGrant, AggregateReplayRf3Tokens.PayloadUseGrant,
                AggregateReplayRf3Tokens.PayloadWriteGrant, AggregateReplayRf3Tokens.HeaderReadGrant,
                AggregateReplayRf3Tokens.HeaderUseGrant, AggregateReplayRf3Tokens.HeaderWriteGrant]);
        var secret = await ConfigureWorkerAsync(administrator, worker, cancellationToken);
        return new(partition, worker, secret);
    }

    internal async Task<(PrincipalRecord Worker, string Secret)> CreateMissingGrantWorkerAsync(
        ClusterFixture fixture, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var principal = Worker with
        {
            Id = AggregateReplayRf3Tokens.RestrictedWorkerIdPrefix
                + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            FieldGrants = [.. Worker.FieldGrants.Where(grant => grant != AggregateReplayRf3Tokens.PayloadUseGrant)]
        };
        var secret = await ConfigureWorkerAsync(administrator, principal, cancellationToken);
        return (principal, secret);
    }

    internal async Task<CommitReceipt> AppendInitialAsync(KeyLoadClient client, Guid commandId,
        CancellationToken cancellationToken)
        => await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(
            new(commandId, Partition, [new AppendEvents(AggregateReplayRf3Tokens.StreamSet,
                AggregateReplayRf3Tokens.StreamId, InitialEvents(), ExpectedStreamRevision.NoStream)]), cancellationToken));

    internal CommandRequest InitialSnapshotCommand(Guid commandId)
        => new(commandId, Partition, [new StoreAggregateSnapshot(AggregateReplayRf3Tokens.StreamSet,
            AggregateReplayRf3Tokens.StreamId, AggregateReplayRf3Tokens.SnapshotSourceRevision,
            AggregateReplayRf3Tokens.Reducer, AggregateReplayRf3Tokens.StateSchema,
            AggregateReplayRf3Tokens.InitialState, 0, AggregateReplayRf3Tokens.Generation)]);

    internal CommandRequest FailoverCommand(Guid commandId)
        => new(commandId, Partition,
        [
            new AppendEvents(AggregateReplayRf3Tokens.StreamSet, AggregateReplayRf3Tokens.StreamId,
                FailoverEvents(), ExpectedRevision: ExpectedStreamRevision.Exact(
                    AggregateReplayRf3Tokens.InitialTailRevision), Generation: AggregateReplayRf3Tokens.Generation),
            new StoreAggregateSnapshot(AggregateReplayRf3Tokens.StreamSet, AggregateReplayRf3Tokens.StreamId,
                AggregateReplayRf3Tokens.ExtendedSnapshotSourceRevision, AggregateReplayRf3Tokens.Reducer,
                AggregateReplayRf3Tokens.StateSchema,
                AggregateReplayRf3Tokens.FailoverState, AggregateReplayRf3Tokens.FirstSnapshotVersion,
                AggregateReplayRf3Tokens.Generation)
        ]);

    internal ReadAggregateReplayRequest ReadRequest(int maximumEvents = AggregateReplayRf3Tokens.TailCount,
        string reducer = AggregateReplayRf3Tokens.Reducer, int schemaVersion = AggregateReplayRf3Tokens.StateSchema,
        long generation = AggregateReplayRf3Tokens.Generation)
        => new(Stream with { Generation = generation }, reducer, schemaVersion, MaximumEvents: maximumEvents);

    internal async Task RevokeAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var revoked = Worker with { Revoked = true, PolicyEpoch = Worker.PolicyEpoch + 1 };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            revoked, cancellationToken));
    }

    internal async Task RepairGrantAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var repaired = Worker with { Revoked = false, PolicyEpoch = Worker.PolicyEpoch + RepairEpochAdvance };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            repaired, cancellationToken));
    }

    private const long RepairEpochAdvance = 2;

    private static ImmutableArray<EventData> InitialEvents() =>
    [
        Event(AggregateReplayRf3Tokens.CreatedEventId, 1),
        Event(AggregateReplayRf3Tokens.PaidEventId, 2),
        Event(AggregateReplayRf3Tokens.ShippedEventId, 3)
    ];

    private static ImmutableArray<EventData> FailoverEvents() =>
    [
        Event(AggregateReplayRf3Tokens.DispatchedEventId, 4),
        Event(AggregateReplayRf3Tokens.DeliveredEventId, 5)
    ];

    private static EventData Event(string eventId, int amount)
    {
        var payload = "{\"" + AggregateReplayRf3Tokens.IncrementProperty + "\":"
            + amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\""
            + AggregateReplayRf3Tokens.SecretProperty + "\":\"" + AggregateReplayRf3Tokens.Secret + "\"}";
        var headers = "{\"" + AggregateReplayRf3Tokens.PrivateHeaderProperty + "\":\""
            + AggregateReplayRf3Tokens.PrivateHeader + "\"}";
        return new(eventId, AggregateReplayRf3Tokens.EventType, payload, headers);
    }

    private static async Task<string> ConfigureWorkerAsync(KeyLoadClient administrator, PrincipalRecord worker,
        CancellationToken cancellationToken)
    {
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            worker, cancellationToken));
        var keyId = AggregateReplayRf3Tokens.CredentialIdPrefix
            + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var verifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(),
            new(keyId, worker.Id, verifier), cancellationToken));
        return secret;
    }
}
