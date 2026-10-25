using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueWorkerClusterCompletion
{
    private const string Missing = "The actual worker completion observation is absent.";

    internal static Result<CommitReceipt> Require(QueueWorkerOperationObservation original)
        => original.Completion ?? throw new InvalidOperationException(Missing);

    internal static async Task<CommitReceipt> SuccessAsync(QueueWorkerOperationObservation original)
    {
        var actual = Require(original);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(actual);
        await Assert.That(actual.Problem).IsNull();
        return receipt;
    }
}
