using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3PublicAssertions
{
    internal static Task ReplayAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
            async callers =>
            {
                await CreationsAsync(callers, seed, token).ConfigureAwait(false);
                foreach (var original in seed.Originals)
                {
                    await SqlRf3Protocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                        await callers.Sdk.CommitAsync(original.Command, token).ConfigureAwait(false)));
                    await SqlRf3Protocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                        await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token).ConfigureAwait(false))).Value);
                    await DocumentAsync(callers, original.Document.Reference, original.Document, token).ConfigureAwait(false);
                }
                return true;
            }, token);

    internal static Task LateFailureAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
            async callers =>
            {
                var actual = await callers.Sdk.CommitAsync(seed.LateFailure, token).ConfigureAwait(false);
                await Assert.That(actual.IsFailed).IsTrue();
                await Assert.That(actual.Value).IsNull();
                await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(actual.Problem, ErrorCode.RevisionConflict,
                    ResourcePartitionInventoryRf3Protocol.RevisionDetail).ConfigureAwait(false);
                var replay = await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, seed.LateFailure, token).ConfigureAwait(false);
                await PartitionMovementPublicRefusalRf3Oracle.McpAsync(replay, ErrorCode.RevisionConflict,
                    PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail).ConfigureAwait(false);
                var absent = new EntityRef(seed.LateFailure.Partition, ResourcePartitionInventoryRf3Protocol.First,
                    ResourcePartitionInventoryRf3Protocol.LateId);
                await DocumentAsync(callers, absent, null, token).ConfigureAwait(false);
                foreach (var original in seed.Originals)
                {
                    await DocumentAsync(callers, original.Document.Reference, original.Document, token).ConfigureAwait(false);
                }
                return true;
            }, token);

    private static async Task CreationsAsync(RequestCqrsRf3Callers callers,
        ResourcePartitionInventoryRf3Seed seed, CancellationToken token)
    {
        foreach (var creation in seed.Creations)
        {
            await SqlRf3Protocol.EqualAsync(creation.Request.Definition, await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.ConfigureResourceAsync(creation.CommandId, creation.Request, token).ConfigureAwait(false)));
            await SqlRf3Protocol.EqualAsync(creation.Request.Definition,
                await ResourcePartitionInventoryRf3Metadata.ConfigureAsync(callers, creation, token).ConfigureAwait(false));
        }
    }

    internal static async Task DocumentAsync(RequestCqrsRf3Callers callers, EntityRef reference,
        DocumentResult? expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
            await callers.Sdk.GetAsync(reference, token).ConfigureAwait(false)).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token).ConfigureAwait(false))
            .ConfigureAwait(false)).Value);
    }
}
