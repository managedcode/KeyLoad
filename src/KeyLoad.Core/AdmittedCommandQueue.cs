namespace KeyLoad.Core;

// One reader, bounded admission, FIFO within each lane and at most eight control commands before a waiting data command.
public sealed class AdmittedCommandQueue(CommandAdmissionGovernor governor)
{
    private readonly object gate = new();
    private readonly Queue<Pending> commands = [], controls = [];
    private readonly SemaphoreSlim available = new(0);
    private int controlBurst;
    private bool stopped;
    public Pending Enqueue(ReplicatedOperation operation, PrincipalRecord principal, int payloadBytes,
        CancellationToken cancellationToken = default)
    {
        if (principal.Id != operation.PrincipalId)
            throw Errors.Fail(ErrorCode.PermissionDenied, "Command admission requires the verified principal identity.");
        var lease = governor.Reserve(operation.Kind, principal, payloadBytes, operation.PayloadJson.Length, cancellationToken);
        try
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stopped) throw Errors.Fail(ErrorCode.ResourceExhausted, "The node command queue has stopped accepting operations.");
                var pending = new Pending(operation, lease);
                (CommandAdmissionGovernor.IsControl(operation.Kind) ? controls : commands).Enqueue(pending);
                available.Release(); return pending;
            }
        }
        catch { lease.Dispose(); throw; }
    }
    public async ValueTask<Pending?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await available.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            if (controls.Count > 0 && (commands.Count == 0 || controlBurst < 8))
            { controlBurst = Math.Min(controlBurst + 1, 8); return controls.Dequeue(); }
            if (commands.Count > 0) { controlBurst = 0; return commands.Dequeue(); }
            if (stopped) return null;
            throw new InvalidOperationException("The command queue signal is inconsistent.");
        }
    }
    public void Stop()
    {
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
            foreach (var queue in new[] { controls, commands })
                while (queue.TryDequeue(out var pending)) pending.Fail(Errors.Fail(ErrorCode.UnknownWriteOutcome,
                    "The node stopped before returning a command outcome. Query or retry the same command ID."));
            available.Release();
        }
    }
    public sealed class Pending(ReplicatedOperation operation, CommandAdmissionGovernor.Lease lease)
    {
        private readonly TaskCompletionSource<OperationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ReplicatedOperation Operation { get; } = operation;
        public Task<OperationResult> Completion => completion.Task;
        public void Complete(OperationResult result) { lease.Dispose(); completion.TrySetResult(result); }
        public void Fail(Exception exception) { lease.Dispose(); completion.TrySetException(exception); }
    }
}
