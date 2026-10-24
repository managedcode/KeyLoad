namespace KeyLoad.Server.Search;

internal static class AnnUntransferred
{
    internal static void Dispose(FileStream? stream, Exception? primary)
    {
        try
        {
            stream?.Dispose();
        }
        catch (Exception cleanup)
        {
            if (primary is not null)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
    }
}
