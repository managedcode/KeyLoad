using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobCompleteCasPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            var scenario = await RelationalSqlClusterScenario.CreateAsync(callers.Sdk, token);
            var blob = new BlobRef(scenario.Partition, RelationalSqlClusterTokens.Blobs, "complete-cas-public");
            var winner = new BlobCompleteCasPublicUpload(blob, 0L, [11, 13, 17], "77fc4a93b8441879ca5e1feb952834f5f458605a594696ada07173f3b1131528");
            var loser = new BlobCompleteCasPublicUpload(blob, 0L, [19, 23], "7853e8856e95cdad7cea9b99eb08d7acf402204e63a83c672ea617492816660d");
            BlobCompleteCasPublicUpload[] initial = [winner, loser];
            await BlobCompleteCasPublicPublication.StageAsync(callers, winner, initial, null, token);
            await BlobCompleteCasPublicPublication.StageAsync(callers, loser, initial, null, token);
            await BlobCompleteCasPublicPublication.CompleteAsync(callers, winner, initial, false, token);
            await BlobCompleteCasPublicAssertions.RefusedAsync(callers, loser.Complete, initial, winner, token);
            loser.FreshRefused = loser.Complete with
            {
                CommandId = Guid.NewGuid()
            };
            await BlobCompleteCasPublicAssertions.RefusedAsync(callers, loser.FreshRefused, initial, winner, token);
            await BlobCompleteCasPublicReplay.UploadAsync(callers, winner, initial, winner, token);
            await BlobCompleteCasPublicReplay.UploadAsync(callers, loser, initial, winner, token);
            await BlobCompleteCasPublicReplay.RemoveAsync(callers, loser, initial, winner, token);
            await BlobCompleteCasPublicAssertions.RefusedAsync(callers, loser.Complete, initial, winner, token);
            await BlobCompleteCasPublicAssertions.RefusedAsync(callers, loser.FreshRefused, initial, winner, token);
            var healthy = new BlobCompleteCasPublicUpload(blob, 1L, [29, 31, 37, 41], "a238dd5555ab025d4d45e06d5d2425850f2c7c7c809a3e32e74a0871ac956452");
            BlobCompleteCasPublicUpload[] all = [winner, loser, healthy];
            await BlobCompleteCasPublicPublication.StageAsync(callers, healthy, all, winner, token);
            await BlobCompleteCasPublicPublication.CompleteAsync(callers, healthy, all, true, token);
            await BlobCompleteCasPublicReplay.UploadAsync(callers, winner, all, healthy, token);
            await BlobCompleteCasPublicAssertions.RefusedAsync(callers, loser.Complete, all, healthy, token);
            await BlobCompleteCasPublicAssertions.RefusedAsync(callers, loser.FreshRefused, all, healthy, token);
            await BlobCompleteCasPublicReplay.ReclaimAsync(callers, loser, all, healthy, token);
            await BlobCompleteCasPublicReplay.UploadAsync(callers, healthy, all, healthy, token);
            return true;
        }, token);
    }
}
