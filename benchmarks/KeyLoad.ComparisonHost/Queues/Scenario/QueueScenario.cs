using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Queues;

/// <summary>The queue scenario, shared by every message broker and KeyLoad.</summary>
internal static class QueueScenario
{
    internal const string Cycle = "cycle";
    private const string WrongBody = "Wrong body for message ";
    private const string Duplicate = "Message delivered twice: ";
    private const string Lost = "Messages never delivered: ";

    /// <summary>
    /// Each operation publishes one message, receives one (from any client) and acknowledges it. Afterwards every
    /// published message must have been delivered exactly once.
    /// </summary>
    internal static async Task<CaseResult> CycleAsync(IQueueDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        await driver.PrepareAsync(token);
        var deliveries = new int[settings.Operations];
        var result = await OperationLanes.RunAsync(Cycle, settings.Operations, settings.Clients, settings.CaseLimit, async (index, cancellation) =>
        {
            await driver.PublishAsync(QueueDataset.Create(index), cancellation);
            var delivery = await driver.ReceiveAsync(cancellation);
            var number = QueueDataset.Number(delivery.Message.Id);
            if (!string.Equals(delivery.Message.Body, QueueDataset.Create(number).Body, StringComparison.Ordinal))
            {
                throw new InvalidDataException(WrongBody + delivery.Message.Id);
            }
            if (Interlocked.Increment(ref deliveries[number]) != 1)
            {
                throw new InvalidDataException(Duplicate + delivery.Message.Id);
            }
            await driver.AckAsync(delivery, cancellation);
        }, token);
        // Only the messages of operations that ran were published; the case time limit may stop the rest.
        var lost = deliveries.Take((int)result.Operations).Count(count => count == 0);
        return lost == 0 ? result : result.WithVerificationError(Lost + lost, lost);
    }
}
