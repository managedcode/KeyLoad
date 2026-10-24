namespace KeyLoad.Server.OrleansHosting;

/// <summary>Publishes a terminal physical-close permit only after every original borrower cleanup succeeds.</summary>
internal sealed class OrleansNodeClosure
{
    private const int NoCleanupFailures = 0;
    private bool completed;

    internal bool IsJoined => Volatile.Read(ref completed);

    internal void Complete(IReadOnlyCollection<Exception> cleanupFailures)
    {
        if (cleanupFailures.Count == NoCleanupFailures)
        {
            Volatile.Write(ref completed, true);
        }
    }
}
