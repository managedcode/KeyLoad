using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyRf3Setup
{
    internal static async Task<RequiredProcessingPolicyRf3State> RunAsync(RequestCqrsRf3Callers root, bool header, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var state = new RequiredProcessingPolicyRf3State(new("required-tenant-" + suffix, "required-database", "required-domain", "required-partition-" + suffix),
            "required-worker-" + suffix, "required-key-" + suffix + ".credential-at-least-32", header);
        var policy = new SensitiveFieldPolicy("/secret", "private", RequiredProcessingPolicyRf3State.Before);
        var resource = new ResourceDefinition(RequiredProcessingPolicyRf3State.Queue, ResourceKind.WorkQueue, state.Partition.TransactionDomainId)
        {
            FieldPolicies = header ? [] : [policy],
            HeaderPolicies = header ? [policy] : []
        };
        state.Resource = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(state.Partition.TenantId, state.Partition.DatabaseId, resource), token));
        await SqlRf3Protocol.EqualAsync(resource, state.Resource);
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(state.Partition.TenantId, state.Partition.DatabaseId,
                new(RequiredProcessingPolicyRf3State.Derived, ResourceKind.Collection, state.Partition.TransactionDomainId)), token));
        await PrincipalAsync(root, state, 1L, false, token);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            DatabaseEngine.Credential(state.Key.Split('.')[0], state.Worker, state.Key), token));
        await EnqueueAsync(root, state, "first", token);
        return state;
    }
    internal static async Task PrincipalAsync(RequestCqrsRf3Callers root, RequiredProcessingPolicyRf3State state,
        long epoch, bool revoked, CancellationToken token)
    {
        var expected = state.Principal(epoch, revoked);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, RequiredProcessingPolicyRf3State state, string grant, CancellationToken token)
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
        await SqlRf3Protocol.EqualAsync(expected, state.Resource);
    }
    internal static async Task EnqueueAsync(RequestCqrsRf3Callers root, RequiredProcessingPolicyRf3State state, string id, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new EnqueueMessage(RequiredProcessingPolicyRf3State.Queue, id, state.Payload, state.Headers)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(request, token));
        await RequiredProcessingPolicyRf3Values.ReceiptAsync(root, state, request.CommandId,
            [new("enqueue", RequiredProcessingPolicyRf3State.Queue, id, 1L)], receipt, token);
        state.Producers.Add((request, receipt));
    }
}
