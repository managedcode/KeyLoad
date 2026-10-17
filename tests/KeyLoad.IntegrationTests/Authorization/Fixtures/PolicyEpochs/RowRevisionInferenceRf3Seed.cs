using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceRf3Seed
{
    internal static async Task<RowRevisionInferenceRf3State> CreateAsync(RequestCqrsRf3Callers root, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(P.TenantPrefix + suffix, P.Database, P.Domain, suffix);
        ResourceDefinition resource = new(P.Collection, ResourceKind.Collection, P.Domain);
        await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, P.Database, resource), token)));
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(P.Collection, P.Hidden, P.HiddenJson, Access: new(OwnerId: P.Foreign)),
                new PutDocument(P.Collection, P.Own, P.Original, Access: new(OwnerId: P.Reader))]);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(P.PlacementVersion, partition), token));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, placement,
            [new(P.PutReceipt, P.Collection, P.Hidden, P.OriginalRevision), new(P.PutReceipt, P.Collection, P.Own, P.OriginalRevision)]);
        PrincipalRecord reader = new(P.ReaderPrefix + suffix, partition.TenantId,
            [new(P.Database, P.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        {
            RestrictRows = true,
            OwnerId = P.Reader,
            PolicyEpoch = P.PolicyEpoch
        };
        await SqlRf3Protocol.EqualAsync(reader, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), reader, token)));
        var key = reader.Id + P.CredentialSeparator + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            DatabaseEngine.Credential(reader.Id, reader.Id, key), token));
        var state = new RowRevisionInferenceRf3State(partition, reader, key);
        state.RootOriginals.Add((command, receipt));
        await RowRevisionInferenceRf3Current.CaptureOutboxAsync(root, state, P.InitialRecords, token);
        await RowRevisionInferenceRf3Current.RequireAsync(root, state, token);
        return state;
    }
}
