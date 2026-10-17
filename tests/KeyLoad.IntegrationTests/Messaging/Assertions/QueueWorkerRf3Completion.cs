using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Completion
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
