using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class TypedEndpointJoinReverseFlow
{
    private const string Prefix = "typed-reverse-";
    private const string ChangedPrefix = "typed-reverse-conflict-";
    private const string RepairedPrefix = "typed-reverse-repaired-";
    private const string EpochChanged = "The principal policy changed since this command was evaluated.";
    private const long EpochStep = 1;

    internal static Task<TypedEndpointJoinState> WarmAsync(ClusterFixture fixture,
        TypedEndpointJoinState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
    {
        var identity = await CreateIdentityAsync(fixture, admin, state.Seed, token);
        return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, identity.Secret,
            callers => ExecuteAsync(fixture, admin, callers, state, identity, token), token);
    }, token);

    private static async Task<TypedEndpointJoinState> ExecuteAsync(ClusterFixture fixture, RequestCqrsClusterCallers admin,
        RequestCqrsClusterCallers callers, TypedEndpointJoinState state, global::KeyLoad.IntegrationTests.ClientApi.McpPersistedIdentity identity, CancellationToken token)
    {
        var mutation = new GraphToQueueMutation(TypedEndpointJoinProtocol.EmptyQueue,
            TypedEndpointJoinProtocol.Graph, TypedEndpointJoinProtocol.From(state.Seed.Partition), Prefix);
        var denied = new CommandRequest(Guid.NewGuid(), state.Seed.Partition, [mutation]);
        var originalProblem = await RefusedAsync(callers, denied, ErrorCode.PermissionDenied, token);
        await TypedEndpointJoinReverseAssertions.UnchangedAsync(admin, state, null, token);
        var principal = await PolicyAsync(admin, identity.Principal, enabled: true, token);
        var deniedProblem = await FencedAsync(callers, denied, token);
        await TypedEndpointJoinReverseAssertions.UnchangedAsync(admin, state, null, token);
        var command = denied with
        {
            CommandId = Guid.NewGuid()
        };
        var receipt = await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp,
            DeclarativeCompositionSql.Reverse(command, mutation), token);
        var reverse = new TypedEndpointJoinReverseState(identity, principal, denied, originalProblem,
            deniedProblem, command, receipt);
        state = state with
        {
            Reverse = reverse
        };
        await TypedEndpointJoinReverseAssertions.OriginalAsync(fixture, admin, state, token);
        var changed = command with
        {
            Mutations = [mutation with { MessageIdPrefix = ChangedPrefix }]
        };
        await RefusedAsync(callers, changed, ErrorCode.Conflict, token);
        await TypedEndpointJoinReverseAssertions.UnchangedAsync(admin, state, reverse, token);
        principal = await PolicyAsync(admin, principal, enabled: false, token);
        await RefusedAsync(callers, command, ErrorCode.PermissionDenied, token);
        await TypedEndpointJoinReverseAssertions.UnchangedAsync(admin, state, reverse, token);
        principal = await PolicyAsync(admin, principal, enabled: true, token);
        var fencedProblem = await FencedAsync(callers, command, token);
        await TypedEndpointJoinReverseAssertions.UnchangedAsync(admin, state, reverse, token);
        var current = command with
        {
            CommandId = Guid.NewGuid(),
            Mutations = [mutation with { MessageIdPrefix = RepairedPrefix }]
        };
        var currentReceipt = await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp,
            DeclarativeCompositionSql.Reverse(current, (GraphToQueueMutation)current.Mutations[0]), token);
        state = state with
        {
            Reverse = reverse with
            {
                Principal = principal,
                FencedProblem = fencedProblem,
                Current = current,
                CurrentReceipt = currentReceipt
            }
        };
        await TypedEndpointJoinReverseAssertions.OriginalAsync(fixture, admin, state, token);
        return state;
    }

    private static async Task<global::KeyLoad.IntegrationTests.ClientApi.McpPersistedIdentity> CreateIdentityAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers admin, TypedEndpointJoinSeed seed, CancellationToken token)
    {
        var identity = await global::KeyLoad.IntegrationTests.ClientApi.McpPersistedIdentity.CreateAsync(fixture, seed.Partition,
            TypedEndpointJoinProtocol.Graph, Capability.GraphRead | Capability.GraphWrite | Capability.Query, token);
        var expected = seed.Principal with
        {
            Id = identity.Principal.Id,
            PolicyEpoch = checked(identity.Principal.PolicyEpoch + EpochStep)
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        return identity with
        {
            Principal = actual
        };
    }

    private static async Task<PrincipalRecord> PolicyAsync(RequestCqrsClusterCallers admin,
        PrincipalRecord current, bool enabled, CancellationToken token)
    {
        var expected = current with
        {
            PolicyEpoch = checked(current.PolicyEpoch + EpochStep),
            Grants = current.Grants.Select(grant => grant.Resource == TypedEndpointJoinProtocol.EmptyQueue
                ? grant with
                {
                    Capabilities = enabled ? grant.Capabilities | Capability.QueuePublish
                    : grant.Capabilities & ~Capability.QueuePublish
                } : grant).ToImmutableArray()
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        return actual;
    }

    internal static async Task<JsonElement> FencedAsync(RequestCqrsClusterCallers callers,
        CommandRequest command, CancellationToken token)
    {
        var problem = await RefusedAsync(callers, command, ErrorCode.PermissionDenied, token, EpochChanged);
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(EpochChanged);
        return problem;
    }

    internal static async Task<JsonElement> RefusedAsync(RequestCqrsClusterCallers callers,
        CommandRequest command, ErrorCode code, CancellationToken token, string? expectedDetail = null)
    {
        var sql = DeclarativeCompositionSql.Reverse(command, (GraphToQueueMutation)command.Mutations[0]);
        var actual = await callers.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value).IsNull();
        await Assert.That(actual.Problem!.ErrorCode).IsEqualTo(code.ToString());
        var official = await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
        await McpCallerAssertions.ErrorAsync(official, code, dispatched: true);
        if (expectedDetail is not null)
        {
            await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(expectedDetail);
        }
        return JsonSerializer.SerializeToElement(actual.Problem, JsonDefaults.Options);
    }
}
