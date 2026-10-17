namespace KeyLoad.Server;

internal sealed partial class PartitionMovementSourceOwner
{
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closing = true;
            shutdown ??= CloseAsync(captures.Values.Select(value => value.CompletionTask).ToArray());
            return new(shutdown);
        }
    }

    private Task CloseAsync(Task[] capturing)
        => PartitionMovementSourceShutdown.JoinAsync(capturing, stopping, gate, sessions,
            closedMoves, closedAdmissions);
}
