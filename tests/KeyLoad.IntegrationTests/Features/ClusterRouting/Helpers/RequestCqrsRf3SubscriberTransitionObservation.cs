using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal sealed class RequestCqrsRf3SubscriberTransitionObservation
{
    private const int MaximumEventsPerDrain = RequestCqrsRf3Protocol.NodeCount;
    internal const string UnexpectedCompletionMessage =
        "Aspire ended its resource subscriber observation unexpectedly.";
    private const int Node1Mask = 0b001;
    private const int Node2Mask = 0b010;
    private const int Node3Mask = 0b100;
    private const int ExpectedNodeMask = Node1Mask | Node2Mask | Node3Mask;
    private const string ObservationPrefix = "C1_SUBSCRIBERS v=1";
    private const string ObservationFormat = "{0} admitted={1} pre={2} terminal={3} preSeen={4} "
        + "terminalSeen={5} admittedMatch={6} preMatch={7} terminalMatch={8} ambiguous={9} limit={10}";
    private static readonly System.Text.CompositeFormat CachedObservationFormat = System.Text.CompositeFormat.Parse(ObservationFormat);
    private const string TrueFlag = "1";
    private const string FalseFlag = "0";
    private int admissionNodeMask;
    private int completionNodeMask;
    private int preFallbackNodeMask;
    private int terminalNodeMask;
    private int unknownEventCount;
    private bool completionObservationStarted;
    private bool preFallbackObserved;
    private bool terminalObserved;
    private bool eventLimitReached;
    private CancellationToken callerToken;
    private Task<bool>? diagnosticMove;
    private bool diagnosticMovePendingAtOwnedCancellation;
    private bool ownedCancellationRequested;

    internal bool RecordAdmission(string name, bool active)
    {
        var bit = NodeBit(name);
        if (active)
        { admissionNodeMask |= bit; }
        return bit != 0;
    }

    internal void BeginCompletionObservation() => completionObservationStarted = true;

    internal void SetCallerToken(CancellationToken token) => callerToken = token;

    internal void ClaimAdmissionMove(Task<bool> pending)
    {
        if (ReferenceEquals(pending, diagnosticMove))
        { diagnosticMove = null; }
    }

    internal void BeginOwnedCancellation(Task<bool>? pendingMove)
        => diagnosticMovePendingAtOwnedCancellation = diagnosticMove is not null
            && ReferenceEquals(pendingMove, diagnosticMove) && !diagnosticMove.IsCompleted
            && !callerToken.IsCancellationRequested;

    internal void CompleteOwnedCancellation(bool lifetimeCanceled)
        => ownedCancellationRequested = diagnosticMovePendingAtOwnedCancellation && lifetimeCanceled;

    internal bool IsExpectedDiagnosticCancellation(Task<bool> pending)
        => ReferenceEquals(pending, diagnosticMove) && diagnosticMovePendingAtOwnedCancellation
            && ownedCancellationRequested && pending.IsCanceled && !callerToken.IsCancellationRequested;

    internal void MoveJoined(Task<bool> pending)
    {
        if (ReferenceEquals(pending, diagnosticMove))
        { diagnosticMove = null; }
    }

    internal void DrainAvailableEvents(IAsyncEnumerator<LogSubscriber> active, ref Task<bool>? pendingMove,
        ref Task<bool>? lastMove, bool afterOriginalJoin)
    {
        var examined = 0;
        while (examined < MaximumEventsPerDrain)
        {
            var pending = pendingMove;
            if (pending is null)
            {
                pending = active.MoveNextAsync().AsTask();
                pendingMove = pending;
                diagnosticMove = pending;
            }
            if (!pending.IsCompleted)
            { break; }
            if (!pending.IsCompletedSuccessfully)
            {
                MarkAmbiguous();
                return;
            }
            var moved = pending.GetAwaiter().GetResult();
            if (!moved)
            {
                MarkAmbiguous();
                return;
            }
            lastMove = pending;
            pendingMove = null;
            diagnosticMove = null;

            var subscriber = active.Current;
            RecordCompletion(subscriber.Name, subscriber.AnySubscribers);
            examined++;
        }

        if (examined == MaximumEventsPerDrain)
        { MarkLimitReached(); }
        MarkSample(afterOriginalJoin);
    }

    internal void RecordCompletion(string name, bool active)
    {
        if (!completionObservationStarted)
        { return; }
        var bit = NodeBit(name);
        if (bit != 0 && !active)
        { completionNodeMask |= bit; }
        else
        { MarkAmbiguous(); }
    }

    internal void MarkAmbiguous()
    {
        while (true)
        {
            var current = System.Threading.Volatile.Read(ref unknownEventCount);
            if (current >= RequestCqrsRf3Protocol.NodeCount)
            { return; }
            if (System.Threading.Interlocked.CompareExchange(ref unknownEventCount, current + 1, current) == current)
            { return; }
        }
    }

    internal void MarkLimitReached() => eventLimitReached = true;

    internal void MarkSample(bool afterOriginalJoin)
    {
        if (afterOriginalJoin)
        {
            terminalNodeMask = completionNodeMask;
            terminalObserved = true;
        }
        else
        {
            preFallbackNodeMask = completionNodeMask;
            preFallbackObserved = true;
        }
    }

    internal string Format()
        => string.Format(System.Globalization.CultureInfo.InvariantCulture, CachedObservationFormat,
            ObservationPrefix, BitCount(admissionNodeMask), BitCount(preFallbackNodeMask), BitCount(terminalNodeMask),
            Flag(preFallbackObserved), Flag(terminalObserved), Flag(admissionNodeMask == ExpectedNodeMask),
            Flag(preFallbackObserved && admissionNodeMask == preFallbackNodeMask),
            Flag(terminalObserved && admissionNodeMask == terminalNodeMask),
            System.Threading.Volatile.Read(ref unknownEventCount),
            Flag(eventLimitReached));

    private static int BitCount(int value) => System.Numerics.BitOperations.PopCount((uint)value);
    private static string Flag(bool value) => value ? TrueFlag : FalseFlag;

    private static int NodeBit(string name) => name switch
    {
        RequestCqrsRf3Protocol.Node1 => Node1Mask,
        RequestCqrsRf3Protocol.Node2 => Node2Mask,
        RequestCqrsRf3Protocol.Node3 => Node3Mask,
        _ => 0
    };
}
