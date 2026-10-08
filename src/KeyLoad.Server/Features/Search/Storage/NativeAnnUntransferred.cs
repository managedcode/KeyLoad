namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnUntransferred
{
    internal static void Dispose(FileStream? stream, Exception? primary)
    {
        try
        { stream?.Dispose(); }
        catch (Exception cleanup)
        {
            if (primary is not null)
            { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
}
