using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceClusterSeed
{
    internal static async Task<RowRevisionInferenceClusterState> CreateAsync(RequestCqrsClusterCallers root, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(P.TenantPrefix + suffix, P.Database, P.Domain, suffix);
        ResourceDefinition resource = new(P.Collection, ResourceKind.Collection, P.Domain);
        await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, P.Database, resource), token)));
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(P.Collection, P.Hidden, P.HiddenJson, Access: new(OwnerId: P.Foreign)),
                new PutDocument(P.Collection, P.Own, P.Original, Access: new(OwnerId: P.Reader))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, [new(P.PutReceipt, P.Collection, P.Hidden, P.OriginalRevision), new(P.PutReceipt, P.Collection, P.Own, P.OriginalRevision)]);
        PrincipalRecord reader = new(P.ReaderPrefix + suffix, partition.TenantId,
            [new(P.Database, P.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        {
            RestrictRows = true,
            OwnerId = P.Reader,
            PolicyEpoch = P.PolicyEpoch
        };
        await SqlClusterProtocol.EqualAsync(reader, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), reader, token)));
        var key = reader.Id + P.CredentialSeparator + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            DatabaseEngine.Credential(reader.Id, reader.Id, key), token));
        var state = new RowRevisionInferenceClusterState(partition, reader, key);
        state.RootOriginals.Add((command, receipt));
        await RowRevisionInferenceClusterCurrent.CaptureOutboxAsync(root, state, P.InitialRecords, token);
        await RowRevisionInferenceClusterCurrent.RequireAsync(root, state, token);
        return state;
    }
}
