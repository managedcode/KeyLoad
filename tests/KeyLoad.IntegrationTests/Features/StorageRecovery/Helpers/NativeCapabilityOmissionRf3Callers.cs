using System.Net;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NativeCapabilityOmissionRf3Callers
{
    internal static async Task RequireRefusedAsync(PartitionMovementPublicParentRf3Seed seed,
        CommandRequest command, bool official, CancellationToken token)
    {
        if (official)
        {
            var actual = await Assert.ThrowsAsync<HttpRequestException>(() => seed.Official.CallAsync(
                McpCallerTools.DocumentsCommit, command, token));
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.StatusCode).IsEqualTo((HttpStatusCode)Errors.Status(ErrorCode.OwnershipLost));
            return;
        }
        var result = await seed.Source.CommitAsync(command, token).ConfigureAwait(false);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
    }
}
