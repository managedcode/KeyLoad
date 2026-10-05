namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeReaderContractPublication
{
    internal static void Require(ZoneTreeStoreRuntime runtime, int minimumReaderContract)
    {
        if (minimumReaderContract != StoreReaderContract.RuntimeJournal)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        }
        runtime.Gate.EnterWriteLock();
        try
        {
            runtime.Check();
            if (runtime.Identity.MinimumReaderContract == minimumReaderContract)
            {
                return;
            }
            var upgraded = runtime.Identity with { MinimumReaderContract = minimumReaderContract };
            try
            {
                ZoneTreeIdentityFile.Write(Path.Combine(runtime.Options.Directory,
                    ZoneTreePersistenceFormat.IdentityFileName), upgraded);
                runtime.Identity = upgraded;
            }
            catch (Exception)
            {
                runtime.Poisoned = true;
                throw Errors.Fail(ErrorCode.RecoveryRequired, ZoneTreePersistenceFormat.RecoveryRequired);
            }
        }
        finally
        {
            runtime.Gate.ExitWriteLock();
        }
    }
}
