using Aspire.Hosting.ApplicationModel;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Stores only fixed native capture-completion observations for one cleanup owner.</summary>
internal sealed class RequestCqrsResourceCompletionEvidence
{
    private RequestCqrsCompletionCallSnapshot node1;
    private RequestCqrsCompletionCallSnapshot node2;
    private RequestCqrsCompletionCallSnapshot node3;
    private RequestCqrsDrainObservationSnapshot drain;
    private RequestCqrsScopeCompletionSnapshot scope;

    internal void RecordScopeCompletion(RequestCqrsScopeCompletionOwner owner, string node,
        RequestCqrsCompletionCallState state, TaskStatus? status)
    {
        var previous = ReadScopeCompletion(owner, node);
        var current = state == RequestCqrsCompletionCallState.Started
            ? new RequestCqrsCompletionCallSnapshot(state, status, null)
            : previous with { State = state, StatusAtReturn = status };
        scope = WriteScopeCompletion(owner, node, current);
    }

    internal void StartCompletion(string node, TaskStatus? status)
        => SetCompletion(node, RequestCqrsCompletionCallState.Started, status);

    internal void FinishCompletion(string node, bool returned, TaskStatus? status)
        => SetCompletion(node, returned ? RequestCqrsCompletionCallState.Returned
            : RequestCqrsCompletionCallState.Failed, status);

    internal void StartDrain(bool tokenCanceled, TaskStatus? first, TaskStatus? second, TaskStatus? third)
        => drain = drain with
        {
            Started = true,
            TokenCanceledAtStart = tokenCanceled,
            StartNode1 = first,
            StartNode2 = second,
            StartNode3 = third
        };

    internal void FailDrain(bool tokenCanceled, TaskStatus? first, TaskStatus? second, TaskStatus? third)
        => drain = drain with
        {
            TokenCanceledAtFailure = tokenCanceled,
            FailureNode1 = first,
            FailureNode2 = second,
            FailureNode3 = third
        };

    internal void ReturnDrain()
        => drain = drain with { Returned = true };

    internal void EnterFallback(bool captureCanceled)
        => drain = drain with { FallbackEntered = true, CaptureCanceledAtFallback = captureCanceled };

    internal void JoinOriginal(bool captureCanceled, TaskStatus? first, TaskStatus? second, TaskStatus? third)
        => drain = drain with
        {
            OriginalJoined = true,
            JoinedNode1 = first,
            JoinedNode2 = second,
            JoinedNode3 = third,
            CaptureCanceledAfterJoin = captureCanceled
        };

    internal void CompleteResourceStream(ResourceLoggerService logger, ContainerResource resource,
        Func<string, TaskStatus?> statusFor, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? failureObserver, RequestCqrsLifecycleStage stage)
    {
        RequestCqrsLifecycleFailureObserver.Observe(() => StartCompletion(resource.Name, statusFor(resource.Name)),
            failures, failureObserver, stage);
        var returned = false;
        RequestCqrsLifecycleFailureObserver.Observe(() =>
        {
            logger.Complete(resource);
            returned = true;
        }, failures, failureObserver, stage);
        RequestCqrsLifecycleFailureObserver.Observe(() => FinishCompletion(resource.Name, returned,
            statusFor(resource.Name)), failures, failureObserver, stage);
    }

    internal void ObserveDrainFailure(RequestCqrsLifecycleStage stage, Func<string, TaskStatus?> statusFor,
        Action<RequestCqrsLifecycleStage>? failureObserver, CancellationToken cancellationToken)
    {
        var observationFailures = new List<Exception>();
        if (stage == RequestCqrsLifecycleStage.CaptureDrain)
        {
            ServerFailureObserver.Observe(() => FailDrain(cancellationToken.IsCancellationRequested,
                statusFor(RequestCqrsRf3Protocol.Node1), statusFor(RequestCqrsRf3Protocol.Node2),
                statusFor(RequestCqrsRf3Protocol.Node3)), observationFailures);
        }
        ServerFailureObserver.Observe(() => failureObserver?.Invoke(stage), observationFailures);
        ServerFailureObserver.ThrowIfAny(observationFailures);
    }

    internal void RecordOriginalJoin(bool captureCanceled, Func<string, TaskStatus?> statusFor,
        List<Exception> failures, Action<RequestCqrsLifecycleStage>? failureObserver, RequestCqrsLifecycleStage stage)
        => RequestCqrsLifecycleFailureObserver.Observe(() => JoinOriginal(captureCanceled,
            statusFor(RequestCqrsRf3Protocol.Node1), statusFor(RequestCqrsRf3Protocol.Node2),
            statusFor(RequestCqrsRf3Protocol.Node3)), failures, failureObserver, stage);

    internal RequestCqrsCleanupCompletionSnapshot Snapshot()
        => new(node1, node2, node3, drain);

    internal RequestCqrsScopeCompletionSnapshot ScopeSnapshot() => scope;

    internal static string Format(RequestCqrsScopeCompletionSnapshot scope,
        RequestCqrsCleanupCompletionSnapshot cleanup)
        => $"sx={Calls(scope.ExplicitNode1, scope.ExplicitNode2, scope.ExplicitNode3)};sb={Calls(scope.BatchNode1, scope.BatchNode2, scope.BatchNode3)};cc={Calls(cleanup.Node1, cleanup.Node2, cleanup.Node3)};dw={Drain(cleanup.Drain)}";

    private void SetCompletion(string node, RequestCqrsCompletionCallState state, TaskStatus? status)
    {
        var previous = ReadCompletion(node);
        var current = state == RequestCqrsCompletionCallState.Started
            ? new RequestCqrsCompletionCallSnapshot(state, status, null)
            : previous with { State = state, StatusAtReturn = status };
        WriteCompletion(node, current);
    }

    private RequestCqrsCompletionCallSnapshot ReadCompletion(string node) => node switch
    {
        RequestCqrsRf3Protocol.Node1 => node1,
        RequestCqrsRf3Protocol.Node2 => node2,
        RequestCqrsRf3Protocol.Node3 => node3,
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };

    private void WriteCompletion(string node, RequestCqrsCompletionCallSnapshot value)
    {
        switch (node)
        {
            case RequestCqrsRf3Protocol.Node1:
                node1 = value;
                break;
            case RequestCqrsRf3Protocol.Node2:
                node2 = value;
                break;
            case RequestCqrsRf3Protocol.Node3:
                node3 = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node));
        }
    }

    private RequestCqrsCompletionCallSnapshot ReadScopeCompletion(RequestCqrsScopeCompletionOwner owner,
        string node) => (owner, node) switch
        {
            (RequestCqrsScopeCompletionOwner.Explicit, RequestCqrsRf3Protocol.Node1) => scope.ExplicitNode1,
            (RequestCqrsScopeCompletionOwner.Explicit, RequestCqrsRf3Protocol.Node2) => scope.ExplicitNode2,
            (RequestCqrsScopeCompletionOwner.Explicit, RequestCqrsRf3Protocol.Node3) => scope.ExplicitNode3,
            (RequestCqrsScopeCompletionOwner.Batch, RequestCqrsRf3Protocol.Node1) => scope.BatchNode1,
            (RequestCqrsScopeCompletionOwner.Batch, RequestCqrsRf3Protocol.Node2) => scope.BatchNode2,
            (RequestCqrsScopeCompletionOwner.Batch, RequestCqrsRf3Protocol.Node3) => scope.BatchNode3,
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };

    private RequestCqrsScopeCompletionSnapshot WriteScopeCompletion(RequestCqrsScopeCompletionOwner owner,
        string node, RequestCqrsCompletionCallSnapshot value)
        => (owner, node) switch
        {
            (RequestCqrsScopeCompletionOwner.Explicit, RequestCqrsRf3Protocol.Node1) => scope with { ExplicitNode1 = value },
            (RequestCqrsScopeCompletionOwner.Explicit, RequestCqrsRf3Protocol.Node2) => scope with { ExplicitNode2 = value },
            (RequestCqrsScopeCompletionOwner.Explicit, RequestCqrsRf3Protocol.Node3) => scope with { ExplicitNode3 = value },
            (RequestCqrsScopeCompletionOwner.Batch, RequestCqrsRf3Protocol.Node1) => scope with { BatchNode1 = value },
            (RequestCqrsScopeCompletionOwner.Batch, RequestCqrsRf3Protocol.Node2) => scope with { BatchNode2 = value },
            (RequestCqrsScopeCompletionOwner.Batch, RequestCqrsRf3Protocol.Node3) => scope with { BatchNode3 = value },
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };

    private static string Calls(RequestCqrsCompletionCallSnapshot first,
        RequestCqrsCompletionCallSnapshot second, RequestCqrsCompletionCallSnapshot third)
        => $"{Call(first)}/{Call(second)}/{Call(third)}";

    private static string Call(RequestCqrsCompletionCallSnapshot value)
        => $"{CompletionState(value.State)}:{CompactStatus(value.StatusAtStart)}:{CompactStatus(value.StatusAtReturn)}";

    private static string Drain(RequestCqrsDrainObservationSnapshot value)
        => $"{Bit(value.Started)}:{OptionalBit(value.TokenCanceledAtStart)}:{Statuses(value.StartNode1, value.StartNode2, value.StartNode3)}:{OptionalBit(value.TokenCanceledAtFailure)}:{Statuses(value.FailureNode1, value.FailureNode2, value.FailureNode3)}:{OptionalBit(value.Returned)}:{Bit(value.FallbackEntered)}:{OptionalBit(value.CaptureCanceledAtFallback)}:{OptionalBit(value.OriginalJoined)}:{Statuses(value.JoinedNode1, value.JoinedNode2, value.JoinedNode3)}:{OptionalBit(value.CaptureCanceledAfterJoin)}";

    private static string Statuses(TaskStatus? first, TaskStatus? second, TaskStatus? third)
        => $"{CompactStatus(first)}/{CompactStatus(second)}/{CompactStatus(third)}";

    private static string CompactStatus(TaskStatus? status) => status switch
    {
        null => "-",
        TaskStatus.Created => "cr",
        TaskStatus.WaitingForActivation => "wa",
        TaskStatus.WaitingToRun => "wr",
        TaskStatus.Running => "ru",
        TaskStatus.WaitingForChildrenToComplete => "wc",
        TaskStatus.RanToCompletion => "ok",
        TaskStatus.Canceled => "ca",
        TaskStatus.Faulted => "fa",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string CompletionState(RequestCqrsCompletionCallState state) => state switch
    {
        RequestCqrsCompletionCallState.NotStarted => "n",
        RequestCqrsCompletionCallState.Started => "s",
        RequestCqrsCompletionCallState.Returned => "r",
        RequestCqrsCompletionCallState.Failed => "f",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static string OptionalBit(bool? value) => value switch
    {
        null => "-",
        true => "1",
        false => "0"
    };

    private static int Bit(bool value) => value ? 1 : 0;
}
