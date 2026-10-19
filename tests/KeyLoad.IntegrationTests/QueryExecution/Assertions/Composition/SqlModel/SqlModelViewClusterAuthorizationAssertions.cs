using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlModelViewClusterAuthorizationAssertions
{
    private const int EpochIncrement = 1;

    internal static QueryRequest EventPrivateProjection(SqlModelViewClusterScenario scenario)
        => scenario.EventSql() with
        {
            Sql = $"SELECT TOP (1) payload.secret AS secret FROM EVENTS('{SqlModelViewClusterScenario.StreamSet}',"
                + $"'{SqlModelViewClusterScenario.StreamId}') WHERE eventId = '{SqlModelViewClusterScenario.FirstEventId}'"
        };

    internal static QueryRequest QueuePrivateProjection(SqlModelViewClusterScenario scenario)
        => scenario.QueueSql() with
        {
            Sql = $"SELECT TOP (1) payload.secret AS secret FROM QUEUE_MESSAGES('{SqlModelViewClusterScenario.Queue}') "
                + $"WHERE id = '{SqlModelViewClusterScenario.FirstMessageId}'"
        };

    internal static async Task<McpPersistedIdentity> GrantSensitiveReadAsync(KeyLoadClient administrator,
        McpPersistedIdentity identity, CancellationToken cancellationToken, bool use = true)
    {
        var updated = identity.Principal with
        {
            FieldGrants = use
                ? [SqlModelViewClusterScenario.SecretGrant, SqlModelViewClusterScenario.SecretUseGrant]
                : [SqlModelViewClusterScenario.SecretGrant],
            PolicyEpoch = identity.Principal.PolicyEpoch + EpochIncrement
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), updated, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(updated.PolicyEpoch);
        await SqlClusterProtocol.EqualAsync(updated, persisted);
        return identity with
        {
            Principal = persisted
        };
    }

    internal static async Task<McpPersistedIdentity> VerifyQueueReadWithoutUseAsync(
        KeyLoadClient administrator, KeyLoadClient reader, McpOfficialClient mcp,
        SqlModelViewClusterScenario scenario, McpPersistedIdentity identity,
        CancellationToken cancellationToken)
    {
        var before = await SqlModelViewClusterParityAssertions.InspectBothAsync(
            administrator, scenario, cancellationToken);
        var request = scenario.QueueSql();
        var predicates = new[]
        {
            request with { Sql = $"SELECT TOP ({SqlModelViewClusterScenario.RowLimit}) * FROM QUEUE_MESSAGES('{SqlModelViewClusterScenario.Queue}') "
                + $"WHERE payload.secret = '{SqlModelViewClusterScenario.QueueCanary}' "
                + $"ORDER BY id" },
            request with { Sql = $"SELECT TOP ({SqlModelViewClusterScenario.RowLimit}) * FROM QUEUE_MESSAGES('{SqlModelViewClusterScenario.Queue}') "
                + $"WHERE headers.privateHeader = '{SqlModelViewClusterScenario.QueueHeaderCanary}' "
                + $"ORDER BY id" }
        };
        for (var transition = 0; transition < 2; transition++)
        {
            identity = await GrantSensitiveReadAsync(administrator, identity, cancellationToken, use: false);
            var visible = await McpCallerAssertions.SdkSuccessAsync(
                await reader.QueryAsync(request, cancellationToken));
            var visibleMcp = await McpCallerAssertions.SuccessAsync<QueryPage>(
                await mcp.CallAsync(McpCallerTools.QueryExecute, request, cancellationToken));
            await SqlModelViewClusterParityAssertions.AssertQueueRowsAsync(before, visible);
            await SqlModelViewClusterParityAssertions.AssertQueueRowsAsync(before, visibleMcp.Value);
            await SqlClusterProtocol.EqualAsync(visible, visibleMcp.Value);
            foreach (var predicate in predicates)
            {
                await SqlModelViewClusterDenialAssertions.SdkAsync(
                    await reader.QueryAsync(predicate, cancellationToken), identity.Secret);
                await SqlModelViewClusterDenialAssertions.McpAsync(
                    await mcp.CallAsync(McpCallerTools.QueryExecute, predicate, cancellationToken), identity.Secret);
            }
            identity = await GrantSensitiveReadAsync(administrator, identity, cancellationToken);
            foreach (var predicate in predicates)
            {
                var sdk = await McpCallerAssertions.SdkSuccessAsync(
                    await reader.QueryAsync(predicate, cancellationToken));
                var official = await McpCallerAssertions.SuccessAsync<QueryPage>(
                    await mcp.CallAsync(McpCallerTools.QueryExecute, predicate, cancellationToken));
                await SqlModelViewClusterParityAssertions.AssertQueueRowsAsync(before, sdk);
                await SqlModelViewClusterParityAssertions.AssertQueueRowsAsync(before, official.Value);
                await SqlClusterProtocol.EqualAsync(sdk, official.Value);
            }
            var after = await SqlModelViewClusterParityAssertions.InspectBothAsync(
                administrator, scenario, cancellationToken);
            await SqlModelViewClusterParityAssertions.AssertQueueUnchangedAsync(before.First, after.First);
            await SqlModelViewClusterParityAssertions.AssertQueueUnchangedAsync(before.Second, after.Second);
        }
        return identity;
    }

    internal static async Task<McpPersistedIdentity> VerifyEventReadWithoutUseAsync(
        KeyLoadClient administrator, KeyLoadClient reader, McpOfficialClient mcp,
        SqlModelViewClusterScenario scenario, McpPersistedIdentity identity,
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
        SqlModelViewClusterScenario scenario, McpPersistedIdentity identity, bool use,
        CancellationToken cancellationToken)
    {
        var execution = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.ReadStreamAsync(new(scenario.Stream), cancellationToken));
        var request = scenario.EventSql();
        var requests = new[]
        {
            request,
            request with { Sql = $"SELECT TOP ({SqlModelViewClusterScenario.RowLimit}) * FROM EVENTS('{SqlModelViewClusterScenario.StreamSet}',"
                + $"'{SqlModelViewClusterScenario.StreamId}') WHERE payload.secret = '{SqlModelViewClusterScenario.EventCanary}' "
                + $"ORDER BY revision" },
            request with { Sql = $"SELECT TOP ({SqlModelViewClusterScenario.RowLimit}) * FROM EVENTS('{SqlModelViewClusterScenario.StreamSet}',"
                + $"'{SqlModelViewClusterScenario.StreamId}') WHERE headers.privateHeader = '{SqlModelViewClusterScenario.EventHeaderCanary}' "
                + $"ORDER BY revision" }
        };
        foreach (var query in requests)
        {
            if (!use && query != request)
            {
                await SqlModelViewClusterDenialAssertions.SdkAsync(
                    await reader.QueryAsync(query, cancellationToken), identity.Secret);
                await SqlModelViewClusterDenialAssertions.McpAsync(
                    await mcp.CallAsync(McpCallerTools.QueryExecute, query, cancellationToken), identity.Secret);
            }
            else
            {
                var sdk = await McpCallerAssertions.SdkSuccessAsync(
                    await reader.QueryAsync(query, cancellationToken));
                var official = await McpCallerAssertions.SuccessAsync<QueryPage>(
                    await mcp.CallAsync(McpCallerTools.QueryExecute, query, cancellationToken));
                await SqlModelViewClusterParityAssertions.AssertEventRowsAsync(execution.Events, sdk);
                await SqlModelViewClusterParityAssertions.AssertEventRowsAsync(execution.Events, official.Value);
                await SqlClusterProtocol.EqualAsync(sdk, official.Value);
            }
            await SqlClusterProtocol.EqualAsync(execution, await McpCallerAssertions.SdkSuccessAsync(
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
        SqlModelViewClusterScenario scenario, Capability onlyCapability, QueryRequest request,
        CancellationToken cancellationToken)
    {
        var resource = request.Sql.Contains("QUEUE_MESSAGES", StringComparison.Ordinal)
            ? SqlModelViewClusterScenario.Queue : SqlModelViewClusterScenario.StreamSet;
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
