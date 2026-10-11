using System.Text;
using System.Text.Json;
using ModelContextProtocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

/// <summary>Synchronously captures the one expected complete official SDK notification.</summary>
internal sealed class EventFeedTailRf3Progress(int maximumBytes) : IProgress<ProgressNotificationValue>
{
    private const string Invalid = "The original official Tail notification is missing, duplicated or invalid.";
    private readonly Lock gate = new();
    private ProgressNotificationValue? observed;
    private Exception? failure;
    private readonly TaskCompletionSource first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Report(ProgressNotificationValue value)
    {
        lock (gate)
        {
            if (observed is not null || value.Message is null || Encoding.UTF8.GetByteCount(value.Message) > maximumBytes)
            { failure ??= new InvalidOperationException(Invalid); return; }
            observed = value;
            first.TrySetResult();
        }
    }
    internal Task WaitAsync(CancellationToken token) => first.Task.WaitAsync(token);

    internal async Task RequireAsync(EventFeedControlResult original, int expectedTotal = OneDeliveryCount)
    {
        ProgressNotificationValue value;
        lock (gate)
        {
            if (failure is not null)
            { throw failure; }
            value = observed ?? throw new InvalidOperationException(Invalid);
        }
        await Assert.That(value.Progress).IsEqualTo(OneDelivery);
        await Assert.That(value.Total).IsEqualTo((float)expectedTotal);
        var actual = JsonSerializer.Deserialize<EventFeedTailProgress>(value.Message!, JsonDefaults.Options)
            ?? throw new InvalidOperationException(Invalid);
        await Assert.That(actual.Version).IsEqualTo(CurrentVersion);
        await QueryExecution.SqlRf3Protocol.EqualAsync(original, actual.OriginalOfferResult);
    }
    internal async Task RequireAbsentAsync()
    {
        bool absent;
        lock (gate)
        {
            if (failure is not null)
            { throw failure; }
            absent = observed is null;
        }
        await Assert.That(absent).IsTrue();
    }
    private const int OneDeliveryCount = 1;
    private const float OneDelivery = OneDeliveryCount;
    private const int CurrentVersion = 1;
}
