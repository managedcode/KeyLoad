namespace KeyLoad.Replication;

internal static class ReplicaMaterializerFailure
{
    internal static void Record(Lock signals, ref Exception? failure, Exception error)
    {
        lock (signals)
        {
            failure = error;
        }
    }
}
