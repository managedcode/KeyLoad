using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueWorkerOptionsTests
{
    [Test]
    [Arguments(QueueWorkerInvalidPolicy.HandlerLimit)]
    [Arguments(QueueWorkerInvalidPolicy.ReceiveBudget)]
    [Arguments(QueueWorkerInvalidPolicy.RenewalBudget)]
    [Arguments(QueueWorkerInvalidPolicy.ReceiveBytes)]
    [Arguments(QueueWorkerInvalidPolicy.RenewalAtExpiry)]
    [Arguments(QueueWorkerInvalidPolicy.EmptyPoll)]
    [Arguments(QueueWorkerInvalidPolicy.UncertaintyCount)]
    [Arguments(QueueWorkerInvalidPolicy.LeaseDuration)]
    public async Task InvalidCallerPolicyRefusesBeforeSendOrHandler(QueueWorkerInvalidPolicy field)
    {
        using var http = new HttpClient();
        http.Dispose();
        var client = new KeyLoadClient(http, QueueWorkerUnitProtocol.UnusedCredential, UnitClientOptions.Execution());
        var configured = Invalid(field);
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => client.RunQueueAckWorkerAsync(
            QueueWorkerUnitProtocol.Lane, Guid.NewGuid(), RejectHandlerAsync, Options.Create(configured), TimeProvider.System));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(QueueWorkerOptions));
        await Assert.That(failure.Failures).Contains(QueueWorkerOptions.ValidationMessage);
    }

    [Test]
    public async Task PreCancelledValidInvocationJoinsWithoutSendOrHandlerAndPreservesSharedClientOwnership()
    {
        using var http = new HttpClient();
        var originalTimeout = http.Timeout;
        var client = new KeyLoadClient(http, QueueWorkerUnitProtocol.UnusedCredential, UnitClientOptions.Execution());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var result = await client.RunQueueAckWorkerAsync(QueueWorkerUnitProtocol.Lane, Guid.NewGuid(), RejectHandlerAsync,
            Options.Create(new QueueWorkerOptions()), TimeProvider.System, cancelled.Token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.Cancelled);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerUnitProtocol.Initial);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerUnitProtocol.Initial);
        await Assert.That(result.PeakActiveHandlers).IsEqualTo(QueueWorkerUnitProtocol.Initial);
        await Assert.That(result.Operations).IsEmpty();
        await Assert.That(result.Unacknowledged).IsEmpty();
        await Assert.That(result.Failures).IsEmpty();
        http.Timeout = originalTimeout;
    }

    private static QueueWorkerOptions Invalid(QueueWorkerInvalidPolicy field)
    {
        var valid = new QueueWorkerOptions();
        return field switch
        {
            QueueWorkerInvalidPolicy.HandlerLimit => valid with { MaximumConcurrentHandlers = QueueWorkerUnitProtocol.Initial },
            QueueWorkerInvalidPolicy.ReceiveBudget => valid with { MaximumReceiveCalls = QueueWorkerUnitProtocol.Initial },
            QueueWorkerInvalidPolicy.RenewalBudget => valid with { MaximumRenewalCalls = QueueWorkerUnitProtocol.Initial },
            QueueWorkerInvalidPolicy.ReceiveBytes => valid with { MaximumReceiveBytes = valid.MaximumInFlightBytes + QueueWorkerUnitProtocol.ExtraByte },
            QueueWorkerInvalidPolicy.RenewalAtExpiry => valid with { RenewalInterval = TimeSpan.FromSeconds(valid.LeaseSeconds) },
            QueueWorkerInvalidPolicy.EmptyPoll => valid with { EmptyPollDelay = TimeSpan.Zero },
            QueueWorkerInvalidPolicy.UncertaintyCount => valid with { UncertaintyRetries = QueueWorkerUnitProtocol.ExceedsUncertainty },
            QueueWorkerInvalidPolicy.LeaseDuration => valid with { LeaseSeconds = QueueWorkerUnitProtocol.ExceedsLease },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static Task RejectHandlerAsync(QueueWorkerDeliveryContext context, CancellationToken token)
        => throw new InvalidOperationException(QueueWorkerUnitProtocol.CallbackMustNotRun);
}
