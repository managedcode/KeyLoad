using KeyLoad.Client;

namespace KeyLoad.RecoveryTests.Messaging;

internal static class ExternalQueueWorkerCrashScenario
{
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != ExternalQueueWorkerCrashProtocol.Arguments
            || args[ExternalQueueWorkerCrashProtocol.ModeArgumentIndex] != ExternalQueueWorkerCrashProtocol.Mode)
        {
            return false;
        }
        var frame = await ReadFrameAsync().ConfigureAwait(false);
        var input = OrleansSerialization.Deserialize<ExternalQueueWorkerCrashInput>(Convert.FromBase64String(frame));
        using var http = new HttpClient { BaseAddress = new Uri(input.Endpoint), Timeout = Timeout.InfiniteTimeSpan };
        var sdk = new KeyLoadClient(http, input.Credential, CrashExecutionOptions.ClientExecution());
        var policy = CrashExecutionOptions.ExternalQueueWorker();
        _ = await sdk.RunQueueAckWorkerAsync(input.Lane, input.ReceiveId,
            (context, token) => PublishThenHoldAsync(sdk, input, context, token),
            policy, TimeProvider.System).ConfigureAwait(false);
        return true;
    }

    private static async Task PublishThenHoldAsync(KeyLoadClient sdk, ExternalQueueWorkerCrashInput input,
        QueueWorkerDeliveryContext context, CancellationToken token)
    {
        var observed = await sdk.InspectAsync(new(input.Lane, ExternalQueueWorkerCrashProtocol.Message), token).ConfigureAwait(false);
        var expected = new MessageInspection(new(ExternalQueueWorkerCrashProtocol.Message, MessageState.Leased,
            ExternalQueueWorkerCrashProtocol.ClaimAttempt, ExternalQueueWorkerCrashProtocol.LeasedVersion,
            ExternalQueueWorkerCrashProtocol.ReadySequence, null, null, input.PrincipalId, ExternalQueueWorkerCrashProtocol.LeaseVersion,
            context.Delivery.LeaseUntil, DeliveryGeneration: ExternalQueueWorkerCrashProtocol.DeliveryGeneration), ExternalQueueWorkerCrashProtocol.Payload, ExternalQueueWorkerCrashProtocol.Headers);
        if (observed.IsFailed || !JsonDefaults.Serialize(observed.Value.Message).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
        {
            throw new InvalidOperationException(ExternalQueueWorkerCrashProtocol.InvalidInput);
        }
        var actual = new ExternalQueueWorkerCrashClaim(context.Delivery, context.AcknowledgementId, context.FirstRenewalId);
        var frame = Convert.ToBase64String(OrleansSerialization.Serialize(actual));
        await Console.Out.WriteLineAsync(ExternalQueueWorkerCrashProtocol.Marker + frame).ConfigureAwait(false);
        await Console.Out.FlushAsync(token).ConfigureAwait(false);
        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
    }

    private static async Task<string> ReadFrameAsync()
    {
        var buffer = new char[ExternalQueueWorkerCrashProtocol.MaximumFrameCharacters + ExternalQueueWorkerCrashProtocol.FrameOverflowMargin];
        var count = ExternalQueueWorkerCrashProtocol.BufferStart;
        while (count < buffer.Length)
        {
            var read = await Console.In.ReadAsync(buffer.AsMemory(count)).ConfigureAwait(false);
            if (read == ExternalQueueWorkerCrashProtocol.EndOfInput)
            {
                return new(buffer, ExternalQueueWorkerCrashProtocol.BufferStart, count);
            }
            count += read;
        }
        throw new InvalidOperationException(ExternalQueueWorkerCrashProtocol.InvalidInput);
    }
}
