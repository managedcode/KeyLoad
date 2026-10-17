using KeyLoad.Clustering;
using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns only real negative sends inside the original awaited producer callback.</summary>
internal sealed class ControlledLifetimeReplayExchange(PartitionMovementTransportContext context, SiloAddress address,
    string receiverVoter, PartitionMovementTransportRequest original, byte[] carrier,
    CancellationToken originalToken) : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly CancellationTokenSource closed = new();
    private Task<ControlledLifetimeReplayResult>? running;
    private Task? shutdown;
    private int next = FirstNegativeStep;
    private bool active;
    internal int CarrierLength => carrier.Length;
    internal byte[] OriginalCarrier => carrier.ToArray();
    internal string ReceiverVoter => receiverVoter;
    internal string ReceiverSiloAddress => address.ToParsableString();

    internal Task<ControlledLifetimeReplayResult> ExecuteAsync(ControlledLifetimeReplayVariation variation,
        byte[]? donor, CancellationToken caller)
    {
        TaskCompletionSource start;
        Task<ControlledLifetimeReplayResult> result;
        lock (sync)
        {
            if (shutdown is not null || active || next > LastNegativeStep
                || variation != ControlledLifetimeReplayMutation.At(next))
            {
                throw InvalidProof();
            }
            originalToken.ThrowIfCancellationRequested();
            caller.ThrowIfCancellationRequested();
            next += IndexStep;
            active = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            running = RunAsync(start.Task, variation, donor, caller);
            result = running;
        }
        start.SetResult();
        return result;
    }

    private async Task<ControlledLifetimeReplayResult> RunAsync(Task start, ControlledLifetimeReplayVariation variation,
        byte[]? donor, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(originalToken, caller, closed.Token);
        try
        {
            var body = ControlledLifetimeReplayMutation.Encode(original, carrier, variation, donor);
            var missing = variation == ControlledLifetimeReplayVariation.Missing;
            var channel = missing ? PartitionMovementGrainServiceChannel.Phase : PartitionMovementGrainServiceChannel.ControlledLifetime;
            var signature = missing ? context.Sign(body, PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.Effect)
                : context.SignControlledLifetime(body);
            ControlledLifetimeReplayResult? observed = null;
            try
            {
                _ = await context.ExchangeAsync(address, receiverVoter, channel, query: false, body, signature,
                    readOnly: false, linked.Token, (request, reply) => observed = new(request, reply)).ConfigureAwait(false);
            }
            catch (KeyLoadException failure) when (observed is not null
                && failure.Code == ControlledLifetimeReplayMutation.Expected(variation) && observed.Reply.Error == failure.Code)
            {
                return observed;
            }
            throw InvalidProof();
        }
        finally
        {
            lock (sync)
            {
                active = false;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            return new(shutdown ??= JoinAsync(running));
        }
    }

    private async Task JoinAsync(Task? admitted)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(closed.CancelAsync, failures).ConfigureAwait(false);
        if (admitted is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => admitted, failures).ConfigureAwait(false);
        }
        try
        {
            closed.Dispose();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static InvalidOperationException InvalidProof() => new(Invalid);
}
