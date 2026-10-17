using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityPolicy
{
    private const string RotatedPrivateClassification = "private-rotated";

    internal static async Task ResourceAsync(KeyLoadClient source, long version, bool rotated, CancellationToken token)
    {
        var expected = new ResourceDefinition(RemoteDocumentRf3Protocol.Collection, ResourceKind.Collection,
            RemoteDocumentRf3Protocol.Domain)
        {
            FieldPolicies = [new(RemoteDocumentRf3Protocol.Secret, rotated ? RotatedPrivateClassification : RemoteDocumentRf3Protocol.Classification)],
            SchemaVersion = version
        };
        var request = new ConfigureResourceRequest(RemoteDocumentRf3Protocol.Tenant,
            RemoteDocumentRf3Protocol.Database, expected)
        {
            ExpectedSchemaVersion = version - DistributedQueryAuthorityProtocol.VersionStep
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await source.ConfigureResourceAsync(Guid.NewGuid(), request, token)));
    }
    internal static async Task PrincipalAsync(KeyLoadClient destination, DistributedQueryAuthorityState state,
        long epoch, bool query, CancellationToken token)
    {
        var expected = state.Seed.Destination.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [new(RemoteDocumentRf3Protocol.Database, RemoteDocumentRf3Protocol.Collection,
                Capability.DocumentsRead | (query ? Capability.Query : Capability.None))]
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await destination.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
        state.DestinationEpoch = epoch;
    }
}
