using System.Text;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Owns fixed, test-only lifecycle facts without retaining request or log content.</summary>
internal sealed class RequestCqrsLifecycleEvidence
{
    private const int MaximumContextBytes = 2_048;
    private const string ContextPrefix = "C1_LIFECYCLE v=1 ";
    private readonly RequestCqrsNodeReadinessOutcome[] readiness =
    [
        RequestCqrsNodeReadinessOutcome.NotObserved,
        RequestCqrsNodeReadinessOutcome.NotObserved,
        RequestCqrsNodeReadinessOutcome.NotObserved
    ];
    private RequestCqrsLifecycleStage stage = RequestCqrsLifecycleStage.Scenario;
    private RequestCqrsLifecycleStage scenarioPhase = RequestCqrsLifecycleStage.Scenario;
    private RequestCqrsLifecycleStage? firstScenarioPhase;
    private RequestCqrsLifecycleSnapshot? terminal;
    private RequestCqrsRf3Diagnostics? diagnostics;
    private RequestCqrsRf3DiagnosticsSubscriberObserver? observer;
    private RequestCqrsRf3DiagnosticsIndependentConsumer? consumer;
    private CancellationToken callerToken;
    private CancellationToken parentToken;
    private CancellationToken waveToken;
    private bool heldWriteObserved;
    private bool persistedRevocationEntered;
    private readonly RequestCqrsResourceCompletionEvidence completionEvidence = new();

    internal void SetStage(RequestCqrsLifecycleStage value) => stage = value;
    internal void SetScenarioPhase(RequestCqrsLifecycleStage value) => scenarioPhase = value;
    internal void SetWaveToken(CancellationToken value) => waveToken = value;
    internal void SetTokens(CancellationToken caller, CancellationToken parent, CancellationToken wave)
    { callerToken = caller; parentToken = parent; waveToken = wave; }
    internal void BindDiagnostics(RequestCqrsRf3Diagnostics value) => diagnostics = value;
    internal void BindObserver(RequestCqrsRf3DiagnosticsSubscriberObserver value) => observer = value;
    internal void BindConsumer(RequestCqrsRf3DiagnosticsIndependentConsumer value) => consumer = value;
    internal void MarkHeldWriteObserved() => heldWriteObserved = true;
    internal void MarkPersistedRevocationEntered() => persistedRevocationEntered = true;

    internal void RecordScopeCompletion(RequestCqrsScopeCompletionOwner owner, string node,
        RequestCqrsCompletionCallState state, TaskStatus? taskStatus)
        => completionEvidence.RecordScopeCompletion(owner, node, state, taskStatus);

    internal void RecordReadiness(string node, RequestCqrsNodeReadinessOutcome outcome)
    {
        var index = node switch
        {
            RequestCqrsRf3Protocol.Node1 => 0,
            RequestCqrsRf3Protocol.Node2 => 1,
            RequestCqrsRf3Protocol.Node3 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };
        readiness[index] = outcome;
    }

    internal RequestCqrsLifecycleSnapshot Snapshot()
    {
        var capture = diagnostics?.ReadLifecycleSnapshot();
        var observerState = observer?.ReadLifecycleSnapshot();
        var consumerState = consumer?.ReadLifecycleSnapshot();
        return new(stage, capture?.Node1, capture?.Node2, capture?.Node3,
            observerState?.PendingMove, consumerState?.PendingMove,
            callerToken.IsCancellationRequested, parentToken.IsCancellationRequested,
            waveToken.IsCancellationRequested, capture?.LifetimeCancellationRequested ?? false,
            capture?.DrainCancellationRequested ?? false, capture?.FallbackRequested ?? false,
            observerState?.LifetimeCancellationRequested ?? false,
            consumerState?.LifetimeCancellationRequested ?? false,
            readiness[0], readiness[1], readiness[2], heldWriteObserved, persistedRevocationEntered,
            completionEvidence.ScopeSnapshot(), capture?.Completion ?? default);
    }

    internal void RecordFirstFailure()
    { FirstFailureSnapshot ??= Snapshot(); firstScenarioPhase ??= scenarioPhase; }

    internal RequestCqrsLifecycleSnapshot? FirstFailureSnapshot { get; private set; }

    internal void RecordOwnerFailure(RequestCqrsLifecycleStage ownerStage)
    {
        stage = ownerStage;
        RecordFirstFailure();
    }

    internal void RecordFirstFailureIfAny(IReadOnlyCollection<Exception> failures)
    {
        if (failures.Count > 0)
        { RecordFirstFailure(); }
    }

    internal void RecordTerminal()
    { terminal = Snapshot(); }

    internal string FormatBoundedContext()
    {
        var first = Format(FirstFailureSnapshot ?? Snapshot());
        var last = Format(terminal ?? Snapshot());
        var context = ContextPrefix + "phase=" + (firstScenarioPhase ?? scenarioPhase) + " first{" + first + "} terminal{" + last + "}";
        if (Encoding.UTF8.GetByteCount(context) > MaximumContextBytes)
        { throw new InvalidOperationException("The fixed C1 lifecycle context exceeded its byte limit."); }
        return context;
    }

    internal void ThrowWithContext(List<Exception> failures)
    {
        if (failures.Count == 0)
        { return; }
        var hasFatal = failures.Any(failure => !NativeCqrsBoundaryErrors.IsNonFatal(failure));
        string context;
        try
        { context = FormatBoundedContext(); }
        catch (InvalidOperationException error)
        {
            if (hasFatal)
            {
                WriteFatalContextAndRethrow(ContextPrefix + "context-over-limit", failures);
                return;
            }
            failures.Add(error);
            context = ContextPrefix + "context-over-limit";
        }
        if (hasFatal)
        {
            WriteFatalContextAndRethrow(context, failures);
            return;
        }
        throw new AggregateException(context, failures);
    }

    private static void WriteFatalContextAndRethrow(string context, List<Exception> failures)
    {
        try
        { TestContext.Current?.Output.WriteLine(context); }
        finally
        { ServerFailureObserver.ThrowIfAny(failures); }
    }

    private static string Format(RequestCqrsLifecycleSnapshot value)
        => $"s={value.Stage};c1={Status(value.CaptureNode1)};c2={Status(value.CaptureNode2)};c3={Status(value.CaptureNode3)};a={Status(value.AdmissionMove)};i={Status(value.IndependentConsumerMove)};caller={Bit(value.CallerCancellationRequested)};parent={Bit(value.ParentCancellationRequested)};wave={Bit(value.WaveCancellationRequested)};capture={Bit(value.CaptureCancellationRequested)};drain={Bit(value.DrainCancellationRequested)};fallback={Bit(value.CaptureFallbackRequested)};observer={Bit(value.ObserverCancellationRequested)};consumer={Bit(value.ConsumerCancellationRequested)};r1={value.Node1Readiness};r2={value.Node2Readiness};r3={value.Node3Readiness};held={Bit(value.HeldWriteObserved)};revoke={Bit(value.PersistedRevocationEntered)};{RequestCqrsResourceCompletionEvidence.Format(value.ScopeCompletion, value.CleanupCompletion)}";

    private static string Status(TaskStatus? status) => status switch
    {
        null => "NotAdmitted",
        TaskStatus.Created => "Created",
        TaskStatus.WaitingForActivation => "WaitingForActivation",
        TaskStatus.WaitingToRun => "WaitingToRun",
        TaskStatus.Running => "Running",
        TaskStatus.WaitingForChildrenToComplete => "WaitingForChildrenToComplete",
        TaskStatus.RanToCompletion => "RanToCompletion",
        TaskStatus.Canceled => "Canceled",
        TaskStatus.Faulted => "Faulted",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static int Bit(bool value) => value ? 1 : 0;
}
