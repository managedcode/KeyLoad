using KeyLoad.Core;

namespace KeyLoad.Replication;

internal static class ReplicaObservedCanonicalApply
{
    internal static void Apply(DatabaseEngine database, ReplicaEntry entry, Func<ReplicaEntry, IDisposable?> observe)
    {
        var scope = observe(entry);
        try
        {
            database.Apply(entry.Operation!, entry.Index);
        }
        catch (Exception primary)
        {
            try
            {
                scope?.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
        scope?.Dispose();
    }
}
