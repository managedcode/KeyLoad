namespace KeyLoad.Orleans;

internal sealed class OnlineTextPublicationWork(Guid sessionId, Guid commandId)
{
    private readonly Lock gate = new();
    private Task<OperationResult>? original;
    private bool closed;
    internal Guid SessionId { get; } = sessionId;
    internal Guid CommandId { get; } = commandId;

    internal Task<OperationResult> AdmitOriginal(Func<Task<OperationResult>> admit)
    {
        ArgumentNullException.ThrowIfNull(admit);
        lock (gate)
        {
            if (original is null)
            {
                if (closed)
                { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
                original = admit();
            }
            return original;
        }
    }

    internal Task<OperationResult>? CloseAdmissionAndCaptureOriginal()
    {
        lock (gate)
        { closed = true; return original; }
    }

    internal Task<OperationResult>? CapturedOriginal()
    {
        lock (gate)
        { return original; }
    }

    internal Task<OperationResult> RequireOriginal()
    {
        lock (gate)
        { return original ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
    }

    internal async Task<OperationResult> AwaitCallerAsync(CancellationToken token)
    {
        try
        { return await RequireOriginal().WaitAsync(token).ConfigureAwait(true); }
        catch (OperationCanceledException)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, TextIndexMaintenanceProtocol.Interrupted); }
    }
}
