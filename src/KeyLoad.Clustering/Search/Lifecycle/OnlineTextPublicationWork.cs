using ManagedCode.Communication;
namespace KeyLoad.Clustering;

internal sealed class OnlineTextPublicationWork(Guid sessionId, Guid commandId)
{
    private readonly Lock gate = new();
    private Task<Result<object>>? original;
    private bool closed;
    internal Guid SessionId { get; } = sessionId;
    internal Guid CommandId { get; } = commandId;

    internal Task<Result<object>> AdmitOriginal(Func<Task<Result<object>>> admit)
    {
        ArgumentNullException.ThrowIfNull(admit);
        lock (gate)
        {
            if (original is null)
            {
                if (closed)
                {
                    throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
                }
                original = admit();
            }
            return original;
        }
    }

    internal Task<Result<object>>? CloseAdmissionAndCaptureOriginal()
    {
        lock (gate)
        {
            closed = true;
            return original;
        }
    }

    internal Task<Result<object>>? CapturedOriginal()
    {
        lock (gate)
        {
            return original;
        }
    }

    internal Task<Result<object>> RequireOriginal()
    {
        lock (gate)
        {
            return original ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }

    internal async Task<Result<object>> AwaitCallerAsync(CancellationToken token)
    {
        try
        {
            return await RequireOriginal().WaitAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, TextIndexMaintenanceProtocol.Interrupted);
        }
    }
}
