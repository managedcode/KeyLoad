using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class RemoteAnnPublicationRf3Assertions
{
    private static readonly double[] Scores = [1d / 61, 1d / 62, 1d / 63];

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(actual)).IsEquivalentTo(
            NativeSerialization.Serialize(expected), CollectionOrdering.Matching);

    internal static async Task ReadsAsync(KeyLoadClient destination, KeyLoadClient source, McpOfficialClient official,
        KeyLoadClient destinationAdministrator, KeyLoadClient sourceAdministrator, RemoteAnnPublicationRf3State state, long policyEpoch, CancellationToken token)
    {
        var owner = await McpCallerAssertions.SdkSuccessAsync(await destinationAdministrator.StatusAsync(token));
        var caller = await McpCallerAssertions.SdkSuccessAsync(await sourceAdministrator.StatusAsync(token));
        await Assert.That(Guid.Parse(owner.NodeId)).IsEqualTo(state.Original.NodeId);
        await Assert.That(caller.NodeId).IsNotEqualTo(owner.NodeId);
        var wait = state.Wait();
        var indexed = await McpCallerAssertions.SdkSuccessAsync(await destination.WaitForAnnIndexAsync(wait, token));
        await EqualAsync(new WaitForAnnIndexResult(indexed.IndexedToken, state.Original.IndexGeneration,
            RemoteAnnPublicationRf3Protocol.FirstEpoch, policyEpoch), indexed);
        await Assert.That(indexed.IndexedToken.Incarnation).IsEqualTo(state.Original.Placement.Incarnation);
        await Assert.That(indexed.IndexedToken.AtomicPartitionId).IsEqualTo(state.Scenario.Partition.AtomicPartitionId);
        await Assert.That(indexed.IndexedToken.OwnershipEpoch).IsEqualTo(state.Original.Placement.PlacementEpoch);
        await Assert.That(indexed.IndexedToken.Position).IsGreaterThanOrEqualTo(wait.MinimumToken.Position);
        await EqualAsync(indexed, await McpCallerAssertions.SdkSuccessAsync(await source.WaitForAnnIndexAsync(wait, token)));
        await EqualAsync(indexed, (await McpCallerAssertions.SuccessAsync<WaitForAnnIndexResult>(
            await official.CallAsync(WaitForAnnIndexProtocol.Tool, wait, token))).Value);
        var page = await McpCallerAssertions.SdkSuccessAsync(await destination.ApproximateSearchAsync(state.Approximate(), token));
        await Assert.That(page.Version).IsEqualTo(RemoteAnnPublicationRf3Protocol.Version);
        await Assert.That(page.Mode).IsEqualTo(AnnPageMode.Exact);
        await Assert.That(page.RequestedMode).IsEqualTo(AnnPageMode.Approximate);
        await Assert.That(page.CompleteTopK).IsTrue();
        await Assert.That(page.IndexGeneration).IsEqualTo(state.Original.IndexGeneration);
        await Assert.That(page.Documents.Length).IsEqualTo(RemoteAnnPublicationRf3Protocol.Count);
        for (var index = RemoteAnnPublicationRf3Protocol.EmptyCount; index < page.Documents.Length; index++)
        {
            var row = page.Documents[index];
            await Assert.That(row.Document.Reference).IsEqualTo(new EntityRef(state.Scenario.Partition,
                NativeAnnMaintenanceRf3Scenario.Collection, NativeAnnMaintenanceRf3Scenario.Ids[index]));
            await Assert.That(row.Document.Json).IsEqualTo(NativeAnnMaintenanceRf3Scenario.Json);
            await Assert.That(row.Document.Revision).IsEqualTo(RemoteAnnPublicationRf3Protocol.FirstEpoch);
            await Assert.That(row.Document.Redacted).IsFalse();
            await Assert.That(row.Document.RedactedFields).IsEmpty();
            await Assert.That(row.Score).IsEqualTo(Scores[index]);
            await Assert.That(row.Explanation).IsNull();
        }
        await Assert.That(page.Position).IsGreaterThanOrEqualTo(indexed.IndexedToken.Position);
        var after = await McpCallerAssertions.SdkSuccessAsync(await destinationAdministrator.StatusAsync(token));
        await Assert.That(page.Position).IsLessThanOrEqualTo(after.Applied);
        await EqualAsync(page, await McpCallerAssertions.SdkSuccessAsync(await source.ApproximateSearchAsync(state.Approximate(), token)));
        await EqualAsync(page, (await McpCallerAssertions.SuccessAsync<AnnSearchPage>(
            await official.CallAsync(AnnSearchProtocol.Tool, state.Approximate(), token))).Value);
    }

    internal static async Task RefusedAsync(KeyLoadClient source, McpOfficialClient official,
        RemoteAnnPublicationRf3State state, CancellationToken token)
    {
        var search = await source.ApproximateSearchAsync(state.Approximate(), token);
        await Assert.That(search.IsSuccess).IsFalse();
        await Assert.That(search.Value).IsNull();
        await Assert.That(search.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var wait = await source.WaitForAnnIndexAsync(state.Wait(), token);
        await Assert.That(wait.IsSuccess).IsFalse();
        await Assert.That(wait.Value).IsNull();
        await Assert.That(wait.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        _ = await McpCallerAssertions.ErrorAsync(await official.CallAsync(AnnSearchProtocol.Tool,
            state.Approximate(), token), ErrorCode.PermissionDenied, dispatched: true);
        _ = await McpCallerAssertions.ErrorAsync(await official.CallAsync(WaitForAnnIndexProtocol.Tool,
            state.Wait(), token), ErrorCode.PermissionDenied, dispatched: true);
    }
}
