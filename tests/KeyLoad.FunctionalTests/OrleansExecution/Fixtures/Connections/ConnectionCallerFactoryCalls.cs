using KeyLoad.Clustering;
using KeyLoad.Server;
using P = KeyLoad.FunctionalTests.OrleansExecution.ConnectionCallerFactoryProtocol;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionCallerFactoryCalls(ConnectionIdentityCollisionState state,
    ConnectionOperationGate gate, List<Exception> failures, CancellationToken token) : IAsyncDisposable
{
    private readonly List<Task<GrainOperationReply>> originals = [];
    internal ConnectionCalls Registry { get; } = new();
    internal bool AllTerminal => originals.All(task => task.IsCompleted);
    internal int DuplicateInvocations
    {
        get;
        private set;
    }
    internal Task<GrainOperationReply>? DuplicateCreated
    {
        get;
        private set;
    }

    internal Task<GrainOperationReply> Start(PrincipalRecord principal, Guid requestId, CommandRequest command,
        CancellationToken token)
    {
        Task<GrainOperationReply>? created = null;
        var actual = Registry.Retain(requestId, () => created = CreateActual(principal, requestId, command, token));
        if (!ReferenceEquals(actual, created))
        {
            throw new InvalidOperationException(P.Failure);
        }
        return actual;
    }

    internal Task<GrainOperationReply> CreateDuplicate(Guid runtimeRequestId, CancellationToken token)
    {
        DuplicateInvocations++;
        return DuplicateCreated = CreateActual(state.Second, runtimeRequestId, state.Following, token);
    }

    private Task<GrainOperationReply> CreateActual(PrincipalRecord principal, Guid requestId,
        CommandRequest command, CancellationToken token)
    {
        var envelope = state.Scenario.Fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch,
            command.CommandId, command);
        var expiry = state.Scenario.Fixture.Codec.Decode(envelope).Envelope.ExpiresAt;
        if (!state.OriginalExpiries.TryAdd(requestId, expiry))
        {
            throw new InvalidOperationException(P.Duplicate);
        }
        var actual = state.Scenario.InvokeAsync(principal, requestId, command.CommandId, envelope, token);
        originals.Add(actual);
        return actual;
    }

    public async ValueTask DisposeAsync()
    {
        gate.Release.TrySetResult();
        await ServerFailureObserver.ObserveAsync(Registry.StopAsync, failures);
        await ServerFailureObserver.ObserveAsync(() => Registry.JoinAsync(failures, token), failures);
        Task? closing = null;
        await ServerFailureObserver.ObserveAsync(() => closing = state.Scenario.CloseAsync(token), failures);
        if (closing?.IsCompletedSuccessfully == true && AllTerminal)
        {
            ServerFailureObserver.Observe(Registry.Dispose, failures);
        }
        else
        {
            ServerFailureObserver.Observe(() => throw new InvalidOperationException(P.Unjoined), failures);
        }
    }
}
