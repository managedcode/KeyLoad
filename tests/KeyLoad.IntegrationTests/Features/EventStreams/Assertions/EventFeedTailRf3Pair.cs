using System.Text;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ModelContextProtocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

/// <summary>Captures exactly two bounded original public deliveries, with no progress history.</summary>
internal sealed class EventFeedTailRf3Pair(int maximumBytes) : IProgress<ProgressNotificationValue>
{
    private const int FirstDelivery = 1;
    private const int TwoDeliveries = 2;
    private const int CurrentVersion = 1;
    private const string Invalid = "The original two-offer Tail delivery is absent, duplicated or invalid.";
    private readonly Lock gate = new();
    private readonly TaskCompletionSource firstSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private EventFeedControlResult? first;
    private EventFeedControlResult? second;
    private Exception? failure;

    public void Report(ProgressNotificationValue value)
    {
        if (value.Message is null || Encoding.UTF8.GetByteCount(value.Message) > maximumBytes)
        {
            Refuse();
            return;
        }
        EventFeedTailProgress? actual;
        try
        {
            actual = JsonSerializer.Deserialize<EventFeedTailProgress>(value.Message, JsonDefaults.Options);
        }
        catch (JsonException error) { Refuse(error); return; }
        if (actual is null || actual.Version != CurrentVersion || value.Total != TwoDeliveries)
        {
            Refuse();
            return;
        }
        Add(actual.OriginalOfferResult, value.Progress);
    }

    internal void ReportSdk(EventFeedTailProgress value)
    {
        _ = Server.Features.EventStreams.EventFeedTailJsonMeasurement.Measure(value, maximumBytes, JsonDefaults.Options);
        if (value.Version != CurrentVersion)
        {
            Refuse();
            return;
        }
        lock (gate)
        {
            AddLocked(value.OriginalOfferResult, first is null ? FirstDelivery : TwoDeliveries);
        }
    }

    private void Add(EventFeedControlResult value, float ordinal)
    {
        lock (gate)
        {
            AddLocked(value, ordinal);
        }
    }

    private void AddLocked(EventFeedControlResult value, float ordinal)
    {
        if (first is null && ordinal == FirstDelivery)
        {
            first = value;
            firstSignal.TrySetResult();
        }
        else if (first is not null && second is null && ordinal == TwoDeliveries)
        {
            second = value;
        }
        else
        {
            failure ??= new InvalidOperationException(Invalid);
            firstSignal.TrySetException(failure);
        }
    }

    private void Refuse(Exception? original = null)
    {
        lock (gate)
        {
            failure ??= original ?? new InvalidOperationException(Invalid);
            firstSignal.TrySetException(failure);
        }
    }

    internal async Task WaitFirstAsync(Task originalOperation, CancellationToken token)
    {
        var firstTask = firstSignal.Task.WaitAsync(token);
        var settled = await Task.WhenAny(firstTask, originalOperation).ConfigureAwait(false);
        if (ReferenceEquals(settled, originalOperation) && !firstSignal.Task.IsCompleted)
        {
            throw new InvalidOperationException(Invalid);
        }
        await firstTask.ConfigureAwait(false);
    }

    internal async Task RequireFirstAsync(EventFeedControlResult original)
    {
        EventFeedControlResult actual;
        lock (gate)
        {
            if (failure is not null)
            {
                throw failure;
            }
            actual = first ?? throw new InvalidOperationException(Invalid);
        }
        await SqlRf3Protocol.EqualAsync(original, actual);
    }

    internal async Task RequirePausedAsync(EventFeedControlResult original)
    {
        await RequireFirstAsync(original);
        bool absent;
        lock (gate)
        {
            if (failure is not null)
            {
                throw failure;
            }
            absent = second is null;
        }
        await Assert.That(absent).IsTrue();
    }

    internal async Task RequireBothAsync(EventFeedControlResult original, EventFeedControlResult next)
    {
        await RequireFirstAsync(original);
        EventFeedControlResult actual;
        lock (gate)
        {
            if (failure is not null)
            {
                throw failure;
            }
            actual = second ?? throw new InvalidOperationException(Invalid);
        }
        await SqlRf3Protocol.EqualAsync(next, actual);
        await Assert.That(actual.PublicCommandId).IsNotEqualTo(original.PublicCommandId);
        await Assert.That(actual.Receipt.Token.Position).IsGreaterThan(original.Receipt.Token.Position);
    }
}
