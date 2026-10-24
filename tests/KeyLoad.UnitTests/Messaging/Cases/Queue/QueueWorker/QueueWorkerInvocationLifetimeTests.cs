using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueWorkerInvocationLifetimeTests
{
    [Test]
    public async Task ActualDisposedCallerTransportRetainsOriginalPreparedIdentityAndExceptionWithoutResult()
    {
        using var http = new HttpClient();
        var sdk = new KeyLoadClient(http, QueueWorkerUnitProtocol.UnusedCredential, UnitClientOptions.Execution());
        http.Dispose();
        var receiveId = Guid.NewGuid();
        var result = await sdk.RunQueueAckWorkerAsync(QueueWorkerUnitProtocol.Lane, receiveId, RejectHandlerAsync,
            Options.Create(new QueueWorkerOptions()), TimeProvider.System);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.LifecycleFailed);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerUnitProtocol.Initial);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerUnitProtocol.Initial);
        await Assert.That(result.Unacknowledged).IsEmpty();
        var prepared = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Prepared)).HasSingleItem();
        var thrown = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Threw)).HasSingleItem();
        await Assert.That(prepared.Receive!.RequestId).IsEqualTo(receiveId);
        await Assert.That(thrown.Receive).IsEqualTo(prepared.Receive);
        await Assert.That(thrown.InvocationFailure).IsTypeOf<ObjectDisposedException>();
        await Assert.That(thrown.ReceiveFailure).IsNull();
        await Assert.That(thrown.ReceiveToken).IsNull();
        await Assert.That(thrown.Completion).IsNull();
        var failure = await Assert.That(result.Failures).HasSingleItem();
        await Assert.That(ReferenceEquals(failure, thrown.InvocationFailure)).IsTrue();
    }

    private static Task RejectHandlerAsync(QueueWorkerDeliveryContext context, CancellationToken token)
        => throw new InvalidOperationException(QueueWorkerUnitProtocol.CallbackMustNotRun);
}
