using System.Collections.Immutable;
using System.Threading.Channels;

namespace KeyLoad.Comparisons;

internal sealed class OpenLoopRunState(OpenLoopTimeline timeline, int payloadBytes,
    int offeredRatePerSecond, Scenario scenario)
{
    private readonly TimeProvider timeProvider = timeline.TimeProvider;
    private readonly System.Threading.Lock gate = new();
    private readonly int[] sampleIndices = ScaledLatencySample.Indices(OpenLoopRateContract.PlannedOperations,
        OpenLoopRateContract.SampleCapacity);
    private readonly OpenLoopLatencySample?[] samples = new OpenLoopLatencySample[OpenLoopRateContract.SampleCapacity];
    private readonly Dictionary<int, OpenLoopWorkItem> live = [];
    private bool frozen;
    private int notOffered, harnessRejected, timedOutBeforeStart, succeeded, failed;
    private int targetRejected, timedOutAfterStart, unfinishedQueued, unfinishedStarted, started, completed;

    internal void RecordNotOffered(int index, long? decisionTimestamp, long terminalTimestamp)
    {
        const int AdjacentElementOffset = 1;
        const int NoObservedItems = 0;

        lock (gate)
        {
            notOffered = checked(notOffered + AdjacentElementOffset);
            var slot = SampleSlot(index);
            if (slot >= NoObservedItems)
            {
                samples[slot] = new(index, null, timeline.DueOffsetNanoseconds(index),
                    Offset(decisionTimestamp), null, null, timeline.OffsetMilliseconds(terminalTimestamp),
                    payloadBytes, OpenLoopOutcome.NotOffered);
            }
        }
    }

    internal OpenLoopOfferDisposition TryOffer(OpenLoopWorkItem item, ChannelWriter<OpenLoopWorkItem> writer)
    {
        const int AdjacentElementOffset = 1;

        lock (gate)
        {
            if (frozen)
            {
                return OpenLoopOfferDisposition.Frozen;
            }
            item.Phase = OpenLoopItemPhase.Queued;
            item.OfferedTimestamp = timeProvider.GetTimestamp();
            live.Add(item.Index, item);
            if (writer.TryWrite(item))
            {
                return OpenLoopOfferDisposition.Accepted;
            }
            live.Remove(item.Index);
            item.Phase = OpenLoopItemPhase.Terminal;
            harnessRejected = checked(harnessRejected + AdjacentElementOffset);
            RecordTerminalSample(item, OpenLoopOutcome.HarnessRejected, timeProvider.GetTimestamp());
            return OpenLoopOfferDisposition.Rejected;
        }
    }

    internal bool TryStart(OpenLoopWorkItem item, int session, long timestamp)
    {
        const int AdjacentElementOffset = 1;

        lock (gate)
        {
            if (frozen || item.Phase != OpenLoopItemPhase.Queued)
            {
                return false;
            }
            if (timestamp >= item.DeadlineTimestamp)
            {
                item.Phase = OpenLoopItemPhase.Terminal;
                live.Remove(item.Index);
                timedOutBeforeStart = checked(timedOutBeforeStart + AdjacentElementOffset);
                RecordTerminalSample(item, OpenLoopOutcome.TimedOutBeforeStart, timestamp);
                return false;
            }
            item.Phase = OpenLoopItemPhase.Started;
            item.Session = session;
            item.StartedTimestamp = timestamp;
            started = checked(started + AdjacentElementOffset);
            return true;
        }
    }

    internal OpenLoopProgressV1? Complete(OpenLoopWorkItem item, OpenLoopOutcome outcome, long timestamp,
        bool publishProgress)
    {
        const int AdjacentElementOffset = 1;
        const int NoMeasuredRate = 0;

        lock (gate)
        {
            if (frozen || item.Phase != OpenLoopItemPhase.Started)
            {
                return null;
            }
            switch (outcome)
            {
                case OpenLoopOutcome.Succeeded:
                    succeeded = checked(succeeded + AdjacentElementOffset);
                    completed = checked(completed + AdjacentElementOffset);
                    break;
                case OpenLoopOutcome.Failed:
                    failed = checked(failed + AdjacentElementOffset);
                    completed = checked(completed + AdjacentElementOffset);
                    break;
                case OpenLoopOutcome.TargetRejected:
                    targetRejected = checked(targetRejected + AdjacentElementOffset);
                    completed = checked(completed + AdjacentElementOffset);
                    break;
                case OpenLoopOutcome.TimedOutAfterStart:
                    timedOutAfterStart = checked(timedOutAfterStart + AdjacentElementOffset);
                    completed = checked(completed + AdjacentElementOffset);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome));
            }
            item.Phase = OpenLoopItemPhase.Terminal;
            live.Remove(item.Index);
            RecordTerminalSample(item, outcome, timestamp);
            return publishProgress && completed % OpenLoopRateContract.ProgressInterval == NoMeasuredRate
                ? new(completed, OpenLoopRateContract.PlannedOperations, started, offeredRatePerSecond, scenario)
                : null;
        }
    }

    internal void Freeze(long timestamp)
    {
        const int AdjacentElementOffset = 1;

        lock (gate)
        {
            if (frozen)
            {
                return;
            }
            frozen = true;
            foreach (var item in live.Values)
            {
                var outcome = item.Phase == OpenLoopItemPhase.Queued
                    ? OpenLoopOutcome.UnfinishedQueued
                    : OpenLoopOutcome.UnfinishedStarted;
                if (outcome == OpenLoopOutcome.UnfinishedQueued)
                {
                    unfinishedQueued = checked(unfinishedQueued + AdjacentElementOffset);
                }
                else
                {
                    unfinishedStarted = checked(unfinishedStarted + AdjacentElementOffset);
                }
                item.Phase = OpenLoopItemPhase.Terminal;
                RecordTerminalSample(item, outcome, timestamp);
            }
            live.Clear();
        }
    }

    internal OpenLoopStateSnapshot Snapshot(double elapsedSeconds)
    {
        lock (gate)
        {
            var accounting = OpenLoopAccountingValidator.ValidateAndCreate(notOffered, harnessRejected,
                timedOutBeforeStart, succeeded, failed, targetRejected, timedOutAfterStart,
                unfinishedQueued, unfinishedStarted, started, completed);
            var retained = samples.Where(sample => sample is not null).Select(sample => sample!).ToImmutableArray();
            return new(accounting, retained, elapsedSeconds);
        }
    }

    internal int SampleSlotFor(int index) => SampleSlot(index);

    private void RecordTerminalSample(OpenLoopWorkItem item, OpenLoopOutcome outcome, long timestamp)
    {
        const int NoObservedItems = 0;

        if (item.SampleSlot < NoObservedItems)
        {
            return;
        }
        samples[item.SampleSlot] = new(item.Index, item.Session < NoObservedItems ? null : item.Session,
            item.DueOffsetNanoseconds, timeline.OffsetMilliseconds(item.DecisionTimestamp),
            timeline.OffsetMilliseconds(item.OfferedTimestamp),
            item.StartedTimestamp == NoObservedItems ? null : timeline.OffsetMilliseconds(item.StartedTimestamp),
            timeline.OffsetMilliseconds(timestamp), item.PayloadBytes, outcome);
    }

    private int SampleSlot(int index) => Array.BinarySearch(sampleIndices, index);
    private double? Offset(long? timestamp) => timestamp is { } value ? timeline.OffsetMilliseconds(value) : null;
}

internal enum OpenLoopOfferDisposition
{
    Accepted,
    Rejected,
    Frozen
}

internal sealed record OpenLoopStateSnapshot(OpenLoopOperationAccounting Accounting,
    ImmutableArray<OpenLoopLatencySample> Samples, double ElapsedSeconds);
