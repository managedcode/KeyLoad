using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.EventStreams;

internal sealed record AggregateReplayClusterScenario(PartitionRef Partition, PrincipalRecord Worker, string WorkerSecret)
{
    internal StreamRef Stream => new(Partition, AggregateReplayClusterTokens.StreamSet,
        AggregateReplayClusterTokens.StreamId, AggregateReplayClusterTokens.Generation);

    internal static async Task<AggregateReplayClusterScenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(AggregateReplayClusterTokens.TenantPrefix
            + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat), AggregateReplayClusterTokens.Database,
            AggregateReplayClusterTokens.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var resource = new ResourceDefinition(AggregateReplayClusterTokens.StreamSet,
            ResourceKind.StreamSet, partition.TransactionDomainId)
        {
            FieldPolicies = [new(AggregateReplayClusterTokens.PayloadSecretPath,
                AggregateReplayClusterTokens.PayloadClassification, AggregateReplayClusterTokens.PayloadReadGrant,
                AggregateReplayClusterTokens.PayloadUseGrant, AggregateReplayClusterTokens.PayloadWriteGrant,
                RequiredForProcessing: false)],
            HeaderPolicies = [new(AggregateReplayClusterTokens.HeaderSecretPath,
                AggregateReplayClusterTokens.HeaderClassification, AggregateReplayClusterTokens.HeaderReadGrant,
                AggregateReplayClusterTokens.HeaderUseGrant, AggregateReplayClusterTokens.HeaderWriteGrant,
                RequiredForProcessing: false)]
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), cancellationToken));
        var principalId = AggregateReplayClusterTokens.WorkerIdPrefix
            + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var capabilities = Capability.EventsAppend | Capability.EventsRead | Capability.EventsReplay
            | Capability.EventsSnapshotsManage;
        var worker = new PrincipalRecord(principalId, partition.TenantId,
            [new(partition.DatabaseId, AggregateReplayClusterTokens.StreamSet, capabilities)],
            [AggregateReplayClusterTokens.PayloadReadGrant, AggregateReplayClusterTokens.PayloadUseGrant,
                AggregateReplayClusterTokens.PayloadWriteGrant, AggregateReplayClusterTokens.HeaderReadGrant,
                AggregateReplayClusterTokens.HeaderUseGrant, AggregateReplayClusterTokens.HeaderWriteGrant]);
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
            Id = AggregateReplayClusterTokens.RestrictedWorkerIdPrefix
                + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            FieldGrants = [.. Worker.FieldGrants.Where(grant => grant != AggregateReplayClusterTokens.PayloadUseGrant)]
        };
        var secret = await ConfigureWorkerAsync(administrator, principal, cancellationToken);
        return (principal, secret);
    }

    internal async Task<CommitReceipt> AppendInitialAsync(KeyLoadClient client, Guid commandId,
        CancellationToken cancellationToken)
        => await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(
            new(commandId, Partition, [new AppendEvents(AggregateReplayClusterTokens.StreamSet,
                AggregateReplayClusterTokens.StreamId, InitialEvents(), ExpectedStreamRevision.NoStream)]), cancellationToken));

    internal CommandRequest InitialSnapshotCommand(Guid commandId)
        => new(commandId, Partition, [new StoreAggregateSnapshot(AggregateReplayClusterTokens.StreamSet,
            AggregateReplayClusterTokens.StreamId, AggregateReplayClusterTokens.SnapshotSourceRevision,
            AggregateReplayClusterTokens.Reducer, AggregateReplayClusterTokens.StateSchema,
            AggregateReplayClusterTokens.InitialState, 0, AggregateReplayClusterTokens.Generation)]);

    internal CommandRequest FailoverCommand(Guid commandId)
        => new(commandId, Partition,
        [
            new AppendEvents(AggregateReplayClusterTokens.StreamSet, AggregateReplayClusterTokens.StreamId,
                FailoverEvents(), ExpectedRevision: ExpectedStreamRevision.Exact(
                    AggregateReplayClusterTokens.InitialTailRevision), Generation: AggregateReplayClusterTokens.Generation),
            new StoreAggregateSnapshot(AggregateReplayClusterTokens.StreamSet, AggregateReplayClusterTokens.StreamId,
                AggregateReplayClusterTokens.ExtendedSnapshotSourceRevision, AggregateReplayClusterTokens.Reducer,
                AggregateReplayClusterTokens.StateSchema,
                AggregateReplayClusterTokens.FailoverState, AggregateReplayClusterTokens.FirstSnapshotVersion,
                AggregateReplayClusterTokens.Generation)
        ]);

    internal ReadAggregateReplayRequest ReadRequest(int maximumEvents = AggregateReplayClusterTokens.TailCount,
        string reducer = AggregateReplayClusterTokens.Reducer, int schemaVersion = AggregateReplayClusterTokens.StateSchema,
        long generation = AggregateReplayClusterTokens.Generation)
        => new(Stream with
        {
            Generation = generation
        }, reducer, schemaVersion, MaximumEvents: maximumEvents);

    internal async Task RevokeAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var revoked = Worker with
        {
            Revoked = true,
            PolicyEpoch = Worker.PolicyEpoch + 1
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            revoked, cancellationToken));
    }

    internal async Task RepairGrantAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var repaired = Worker with
        {
            Revoked = false,
            PolicyEpoch = Worker.PolicyEpoch + RepairEpochAdvance
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            repaired, cancellationToken));
    }

    private const long RepairEpochAdvance = 2;

    private static ImmutableArray<EventData> InitialEvents() =>
    [
        Event(AggregateReplayClusterTokens.CreatedEventId, 1),
        Event(AggregateReplayClusterTokens.PaidEventId, 2),
        Event(AggregateReplayClusterTokens.ShippedEventId, 3)
    ];

    private static ImmutableArray<EventData> FailoverEvents() =>
    [
        Event(AggregateReplayClusterTokens.DispatchedEventId, 4),
        Event(AggregateReplayClusterTokens.DeliveredEventId, 5)
    ];

    private static EventData Event(string eventId, int amount)
    {
        var payload = "{\"" + AggregateReplayClusterTokens.IncrementProperty + "\":"
            + amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\""
            + AggregateReplayClusterTokens.SecretProperty + "\":\"" + AggregateReplayClusterTokens.Secret + "\"}";
        var headers = "{\"" + AggregateReplayClusterTokens.PrivateHeaderProperty + "\":\""
            + AggregateReplayClusterTokens.PrivateHeader + "\"}";
        return new(eventId, AggregateReplayClusterTokens.EventType, payload, headers);
    }

    private static async Task<string> ConfigureWorkerAsync(KeyLoadClient administrator, PrincipalRecord worker,
        CancellationToken cancellationToken)
    {
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            worker, cancellationToken));
        var keyId = AggregateReplayClusterTokens.CredentialIdPrefix
            + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var verifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        await McpCallerAssertions.SdkStatusAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(),
            new(keyId, worker.Id, verifier), cancellationToken));
        return secret;
    }
}
