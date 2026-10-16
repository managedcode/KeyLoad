using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlModelViewRf3AuthorizationAssertions
{
    private const int EpochIncrement = 1;

    internal static QueryRequest EventPrivateProjection(SqlModelViewRf3Scenario scenario)
        => scenario.EventSql() with
        {
            Sql = $"SELECT TOP (1) payload.secret AS secret FROM EVENTS('{SqlModelViewRf3Scenario.StreamSet}',"
                + $"'{SqlModelViewRf3Scenario.StreamId}') WHERE eventId = '{SqlModelViewRf3Scenario.FirstEventId}'"
        };

    internal static QueryRequest QueuePrivateProjection(SqlModelViewRf3Scenario scenario)
        => scenario.QueueSql() with
        {
            Sql = $"SELECT TOP (1) payload.secret AS secret FROM QUEUE_MESSAGES('{SqlModelViewRf3Scenario.Queue}') "
                + $"WHERE id = '{SqlModelViewRf3Scenario.FirstMessageId}'"
        };

    internal static async Task<McpPersistedIdentity> GrantSensitiveReadAsync(KeyLoadClient administrator,
        McpPersistedIdentity identity, CancellationToken cancellationToken, bool use = true)
    {
        var updated = identity.Principal with
        {
            FieldGrants = use
                ? [SqlModelViewRf3Scenario.SecretGrant, SqlModelViewRf3Scenario.SecretUseGrant]
                : [SqlModelViewRf3Scenario.SecretGrant],
            PolicyEpoch = identity.Principal.PolicyEpoch + EpochIncrement
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), updated, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(updated.PolicyEpoch);
        await SqlRf3Protocol.EqualAsync(updated, persisted);
        return identity with
        {
            Principal = persisted
        };
    }

    internal static async Task<McpPersistedIdentity> VerifyQueueReadWithoutUseAsync(
        KeyLoadClient administrator, KeyLoadClient reader, McpOfficialClient mcp,
        SqlModelViewRf3Scenario scenario, McpPersistedIdentity identity,
        CancellationToken cancellationToken)
    {
        var before = await SqlModelViewRf3ParityAssertions.InspectBothAsync(
            administrator, scenario, cancellationToken);
        var request = scenario.QueueSql();
        var predicates = new[]
        {
            request with { Sql = $"SELECT TOP ({SqlModelViewRf3Scenario.RowLimit}) * FROM QUEUE_MESSAGES('{SqlModelViewRf3Scenario.Queue}') "
                + $"WHERE payload.secret = '{SqlModelViewRf3Scenario.QueueCanary}' "
                + $"ORDER BY id" },
            request with { Sql = $"SELECT TOP ({SqlModelViewRf3Scenario.RowLimit}) * FROM QUEUE_MESSAGES('{SqlModelViewRf3Scenario.Queue}') "
                + $"WHERE headers.privateHeader = '{SqlModelViewRf3Scenario.QueueHeaderCanary}' "
                + $"ORDER BY id" }
        };
        for (var transition = 0; transition < 2; transition++)
        {
            identity = await GrantSensitiveReadAsync(administrator, identity, cancellationToken, use: false);
            var visible = await McpCallerAssertions.SdkSuccessAsync(
                await reader.QueryAsync(request, cancellationToken));
            var visibleMcp = await McpCallerAssertions.SuccessAsync<QueryPage>(
                await mcp.CallAsync(McpCallerTools.QueryExecute, request, cancellationToken));
            await SqlModelViewRf3ParityAssertions.AssertQueueRowsAsync(before, visible);
            await SqlModelViewRf3ParityAssertions.AssertQueueRowsAsync(before, visibleMcp.Value);
            await SqlRf3Protocol.EqualAsync(visible, visibleMcp.Value);
            foreach (var predicate in predicates)
            {
                await SqlModelViewRf3DenialAssertions.SdkAsync(
                    await reader.QueryAsync(predicate, cancellationToken), identity.Secret);
                await SqlModelViewRf3DenialAssertions.McpAsync(
                    await mcp.CallAsync(McpCallerTools.QueryExecute, predicate, cancellationToken), identity.Secret);
            }
            identity = await GrantSensitiveReadAsync(administrator, identity, cancellationToken);
            foreach (var predicate in predicates)
            {
                var sdk = await McpCallerAssertions.SdkSuccessAsync(
                    await reader.QueryAsync(predicate, cancellationToken));
                var official = await McpCallerAssertions.SuccessAsync<QueryPage>(
                    await mcp.CallAsync(McpCallerTools.QueryExecute, predicate, cancellationToken));
                await SqlModelViewRf3ParityAssertions.AssertQueueRowsAsync(before, sdk);
                await SqlModelViewRf3ParityAssertions.AssertQueueRowsAsync(before, official.Value);
                await SqlRf3Protocol.EqualAsync(sdk, official.Value);
            }
            var after = await SqlModelViewRf3ParityAssertions.InspectBothAsync(
                administrator, scenario, cancellationToken);
            await SqlModelViewRf3ParityAssertions.AssertQueueUnchangedAsync(before.First, after.First);
            await SqlModelViewRf3ParityAssertions.AssertQueueUnchangedAsync(before.Second, after.Second);
        }
        return identity;
    }

    internal static async Task<McpPersistedIdentity> VerifyEventReadWithoutUseAsync(
        KeyLoadClient administrator, KeyLoadClient reader, McpOfficialClient mcp,
        SqlModelViewRf3Scenario scenario, McpPersistedIdentity identity,
        CancellationToken cancellationToken)
    {
        for (var transition = 0; transition < 2; transition++)
        {
            identity = await GrantSensitiveReadAsync(administrator, identity, cancellationToken, use: false);
            await VerifyEventQueriesAsync(administrator, reader, mcp, scenario, identity, false, cancellationToken);
            identity = await GrantSensitiveReadAsync(administrator, identity, cancellationToken);
            await VerifyEventQueriesAsync(administrator, reader, mcp, scenario, identity, true, cancellationToken);
        }
        return identity;
    }

    private static async Task VerifyEventQueriesAsync(
        KeyLoadClient administrator, KeyLoadClient reader, McpOfficialClient mcp,
        SqlModelViewRf3Scenario scenario, McpPersistedIdentity identity, bool use,
        CancellationToken cancellationToken)
    {
        var native = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.ReadStreamAsync(new(scenario.Stream), cancellationToken));
        var request = scenario.EventSql();
        var requests = new[]
        {
            request,
            request with { Sql = $"SELECT TOP ({SqlModelViewRf3Scenario.RowLimit}) * FROM EVENTS('{SqlModelViewRf3Scenario.StreamSet}',"
                + $"'{SqlModelViewRf3Scenario.StreamId}') WHERE payload.secret = '{SqlModelViewRf3Scenario.EventCanary}' "
                + $"ORDER BY revision" },
            request with { Sql = $"SELECT TOP ({SqlModelViewRf3Scenario.RowLimit}) * FROM EVENTS('{SqlModelViewRf3Scenario.StreamSet}',"
                + $"'{SqlModelViewRf3Scenario.StreamId}') WHERE headers.privateHeader = '{SqlModelViewRf3Scenario.EventHeaderCanary}' "
                + $"ORDER BY revision" }
        };
        foreach (var query in requests)
        {
            if (!use && query != request)
            {
                await SqlModelViewRf3DenialAssertions.SdkAsync(
                    await reader.QueryAsync(query, cancellationToken), identity.Secret);
                await SqlModelViewRf3DenialAssertions.McpAsync(
                    await mcp.CallAsync(McpCallerTools.QueryExecute, query, cancellationToken), identity.Secret);
            }
            else
            {
                var sdk = await McpCallerAssertions.SdkSuccessAsync(
                    await reader.QueryAsync(query, cancellationToken));
                var official = await McpCallerAssertions.SuccessAsync<QueryPage>(
                    await mcp.CallAsync(McpCallerTools.QueryExecute, query, cancellationToken));
                await SqlModelViewRf3ParityAssertions.AssertEventRowsAsync(native.Events, sdk);
                await SqlModelViewRf3ParityAssertions.AssertEventRowsAsync(native.Events, official.Value);
                await SqlRf3Protocol.EqualAsync(sdk, official.Value);
            }
            await SqlRf3Protocol.EqualAsync(native, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.ReadStreamAsync(new(scenario.Stream), cancellationToken)));
        }
    }

    internal static async Task RevokeAsync(KeyLoadClient administrator, McpPersistedIdentity identity,
        CancellationToken cancellationToken)
    {
        var revoked = identity.Principal with
        {
            Grants = [],
            Revoked = true,
            PolicyEpoch = identity.Principal.PolicyEpoch + EpochIncrement
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            revoked, cancellationToken));
    }

    internal static async Task AssertDeniedAsync(ClusterFixture fixture, KeyLoadClient sdk,
        McpOfficialClient mcp, QueryRequest request, string secret, CancellationToken cancellationToken)
    {
        var sdkResult = await sdk.QueryAsync(request, cancellationToken);
        await Assert.That(sdkResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => mcp.CallAsync(
            McpCallerTools.QueryExecute, request, cancellationToken));
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node3, secret, cancellationToken);
    }

    internal static async Task AssertMissingGrantDeniedAsync(ClusterFixture fixture,
        SqlModelViewRf3Scenario scenario, Capability onlyCapability, QueryRequest request,
        CancellationToken cancellationToken)
    {
        var resource = request.Sql.Contains("QUEUE_MESSAGES", StringComparison.Ordinal)
            ? SqlModelViewRf3Scenario.Queue : SqlModelViewRf3Scenario.StreamSet;
        var identity = await McpPersistedIdentity.CreateAsync(fixture,
            scenario.Partition, resource, onlyCapability, cancellationToken);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture,
            McpCallerProtocol.Node3, identity.Secret, cancellationToken);
        var result = await sdk.QueryAsync(request, cancellationToken);
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.QueryExecute,
            request, cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }
}
