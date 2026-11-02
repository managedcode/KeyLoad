using ManagedCode.Communication;
namespace KeyLoad.Clustering;

internal sealed class OnlineTextPublicationWork<TResult>(Guid sessionId, Guid commandId)
{
    private readonly Lock gate = new();
    private Task<Result<TResult>>? original;
    private bool closed;
    internal Guid SessionId { get; } = sessionId;
    internal Guid CommandId { get; } = commandId;

    internal Task<Result<TResult>> AdmitOriginal(Func<Task<Result<TResult>>> admit)
    {
        ArgumentNullException.ThrowIfNull(admit) /* KLD0041: BCL null validation accepts the admit Func reference through its object parameter. */;
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

    internal Task<Result<TResult>>? CloseAdmissionAndCaptureOriginal()
    {
        lock (gate)
        {
            closed = true;
            return original;
        }
    }

    internal Task<Result<TResult>>? CapturedOriginal()
    {
        lock (gate)
        {
            return original;
        }
    }

    internal Task<Result<TResult>> RequireOriginal()
    {
        lock (gate)
        {
            return original ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }

    internal async Task<Result<TResult>> AwaitCallerAsync(CancellationToken token)
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
