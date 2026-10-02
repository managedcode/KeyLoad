namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreHandleDisposal
{
    internal static void FailedOpen(ZoneTreeStoreRuntime runtime)
    {
        try
        {
            runtime.Maintainer?.Dispose();
        }
        finally
        {
            DisposeJournalBeforeTree(runtime);
        }
    }

    internal static void Normal(ZoneTreeStoreRuntime runtime)
    {
        try
        {
            runtime.Maintainer?.Dispose();
        }
        finally
        {
            DisposeTreeBeforeJournal(runtime);
        }
    }

    private static void DisposeJournalBeforeTree(ZoneTreeStoreRuntime runtime)
    {
        try
        {
            runtime.Journal?.Dispose();
        }
        finally
        {
            try
            {
                runtime.Tree?.Dispose();
            }
            finally
            {
                runtime.Ownership?.Dispose();
            }
        }
    }

    private static void DisposeTreeBeforeJournal(ZoneTreeStoreRuntime runtime)
    {
        try
        {
            runtime.Tree?.Dispose();
        }
        finally
        {
            try
            {
                runtime.Journal?.Dispose();
            }
            finally
            {
                runtime.Ownership.Dispose();
            }
        }
    }
}
