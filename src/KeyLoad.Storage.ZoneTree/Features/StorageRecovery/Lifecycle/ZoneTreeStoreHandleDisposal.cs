namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreHandleDisposal
{
    internal static void FailedOpen(ZoneTreeStoreRuntime runtime)
    {
        try
        {
            RetireMaintainer(runtime);
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
            RetireMaintainer(runtime);
        }
        finally
        {
            DisposeTreeBeforeJournal(runtime);
        }
    }

    internal static void RetireMaintainer(ZoneTreeStoreRuntime runtime)
    {
        if (runtime.Maintenance is not { } maintenance)
        {
            return;
        }

        try
        {
            maintenance.Dispose();
        }
        finally
        {
            runtime.Maintenance = null;
        }
    }

    internal static void RetireTree(ZoneTreeStoreRuntime runtime)
    {
        if (runtime.Tree is not { } tree)
        {
            return;
        }

        tree.Dispose();
        runtime.Tree = null!;
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
                RetireTree(runtime);
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
            RetireTree(runtime);
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
