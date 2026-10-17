using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Setup
{
    internal static async Task RunAsync(MetadataBootstrapRf3State state, CancellationToken token)
    {
        await MetadataBootstrapRf3Metadata.CaptureAsync(state, token);
        var resource = new ResourceDefinition(MetadataBootstrapRf3State.Collection, ResourceKind.Collection, state.First.TransactionDomainId);
        await MetadataBootstrapRf3Calls.EqualAsync(resource, await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey,
            sdk => sdk.ConfigureResourceAsync(Guid.NewGuid(), new(state.First.TenantId, state.First.DatabaseId, resource), token)));
        state.Identity = await McpPersistedIdentity.CreateAsync(state.Fixture, state.First,
            MetadataBootstrapRf3State.Collection, Capability.DocumentsRead, token);
        await MetadataBootstrapRf3Metadata.BindNextAsync(state, state.First, state.Fixture.AdminKey, token);
        await MetadataBootstrapRf3Data.CommitAsync(state, state.First, "before-loss", token);
        state.BeforeLoss = await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey, sdk => sdk.StatusAsync(token));
        await MetadataBootstrapRf3Authority.ValidateAsync(state, state.BeforeLoss);
        foreach (var node in MetadataBootstrapRf3State.Nodes)
        {
            state.ActiveNode = node;
            var status = await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey, sdk => sdk.StatusAsync(token));
            await MetadataBootstrapRf3Authority.ValidateAsync(state, status);
            state.Statuses.Add(node, status);
        }
    }
}
