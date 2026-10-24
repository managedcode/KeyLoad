using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueWorkerPollBoundaryTests
{
    private const int MaximumEmptyPollSeconds = 1;
    private const long BeyondCeilingTick = 1L;

    [Test]
    public async Task ExactEmptyPollCeilingAdmitsPreCancelledInvocationAndNextTickRefusesBeforeIo()
    {
        using var http = new HttpClient();
        http.Dispose();
        var client = new KeyLoadClient(http, QueueWorkerUnitProtocol.UnusedCredential, UnitClientOptions.Execution());
        var maximum = TimeSpan.FromSeconds(MaximumEmptyPollSeconds);
        var policy = new QueueWorkerOptions
        {
            EmptyPollDelay = maximum
        };
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        var result = await client.RunQueueAckWorkerAsync(QueueWorkerUnitProtocol.Lane, Guid.NewGuid(),
            RejectHandlerAsync, Options.Create(policy), TimeProvider.System, caller.Token);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.Cancelled);
        await Assert.That(result.Received).IsEqualTo(QueueWorkerUnitProtocol.Initial);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerUnitProtocol.Initial);
        await Assert.That(result.PeakActiveHandlers).IsEqualTo(QueueWorkerUnitProtocol.Initial);
        await Assert.That(result.PeakInFlightBytes).IsEqualTo((long)QueueWorkerUnitProtocol.Initial);
        await Assert.That(result.Operations).IsEmpty();
        await Assert.That(result.Unacknowledged).IsEmpty();
        await Assert.That(result.Failures).IsEmpty();
        var invalid = policy with
        {
            EmptyPollDelay = TimeSpan.FromTicks(maximum.Ticks + BeyondCeilingTick)
        };
        var original = Assert.ThrowsExactly<OptionsValidationException>(() => client.RunQueueAckWorkerAsync(
            QueueWorkerUnitProtocol.Lane, Guid.NewGuid(), RejectHandlerAsync, Options.Create(invalid), TimeProvider.System));
        await Assert.That(original.OptionsType).IsEqualTo(typeof(QueueWorkerOptions));
        await Assert.That(original.Failures).Contains(QueueWorkerOptions.ValidationMessage);
    }

    private static Task RejectHandlerAsync(QueueWorkerDeliveryContext context, CancellationToken token)
        => throw new InvalidOperationException(QueueWorkerUnitProtocol.CallbackMustNotRun);
}
