using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.ResourceExecution;
using KeyLoad.Core;

namespace KeyLoad.ClusterTests.Authorization;

internal static class RequiredProcessingPolicyClusterSetup
{
    internal static async Task<RequiredProcessingPolicyClusterState> RunAsync(RequestCqrsClusterCallers root, bool header, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var state = new RequiredProcessingPolicyClusterState(new("required-tenant-" + suffix, "required-database", "required-domain", "required-partition-" + suffix),
            "required-worker-" + suffix, "required-key-" + suffix + ".credential-at-least-32", header);
        var policy = new SensitiveFieldPolicy("/secret", "private", RequiredProcessingPolicyClusterState.Before);
        var resource = new ResourceDefinition(RequiredProcessingPolicyClusterState.Queue, ResourceKind.WorkQueue, state.Partition.TransactionDomainId)
        {
            FieldPolicies = header ? [] : [policy],
            HeaderPolicies = header ? [policy] : []
        };
        state.Resource = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(state.Partition.TenantId, state.Partition.DatabaseId, resource), token));
        await SqlClusterProtocol.EqualAsync(resource, state.Resource);
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId,
            new(RequiredProcessingPolicyClusterState.Derived, ResourceKind.Collection, state.Partition.TransactionDomainId));
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(creationId, creation, token));
        state.DerivedLifetime = ResourceCreationLifetime.Create(creationId, creation);
        await PrincipalAsync(root, state, 1L, false, token);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            AuthorizationExecution.Credential(state.Key.Split('.')[0], state.Worker, state.Key), token));
        await EnqueueAsync(root, state, "first", token);
        return state;
    }
    internal static async Task PrincipalAsync(RequestCqrsClusterCallers root, RequiredProcessingPolicyClusterState state,
        long epoch, bool revoked, CancellationToken token)
    {
        var expected = state.Principal(epoch, revoked);
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, RequiredProcessingPolicyClusterState state, string grant, CancellationToken token)
    {
        var policy = new SensitiveFieldPolicy("/secret", "private", grant, RequiredForProcessing: true);
        var expected = state.Resource with
        {
            SchemaVersion = state.Resource.SchemaVersion + 1L,
            FieldPolicies = state.Header ? [] : [policy],
            HeaderPolicies = state.Header ? [policy] : []
        };
        state.Resource = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, expected)
            {
                ExpectedSchemaVersion = state.Resource.SchemaVersion
            }, token));
        await SqlClusterProtocol.EqualAsync(expected, state.Resource);
    }
    internal static async Task EnqueueAsync(RequestCqrsClusterCallers root, RequiredProcessingPolicyClusterState state, string id, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new EnqueueMessage(RequiredProcessingPolicyClusterState.Queue, id, state.Payload, state.Headers)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(request, token));
        await RequiredProcessingPolicyClusterValues.ReceiptAsync(state, request.CommandId,
            [new("enqueue", RequiredProcessingPolicyClusterState.Queue, id, 1L)], receipt);
        state.Producers.Add((request, receipt));
    }
}
