using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCheckpointGeneration
{
    internal static void Replace(ZoneTreeStoreRuntime runtime, string temporary, long cut, bool replaceTree)
    {
        string? retiredTree;
        try
        {
            retiredTree = (Prepare(runtime, replaceTree)).Value;
            Swap(runtime, temporary, cut, replaceTree);
        }
        catch (Exception original)
        {
            runtime.Poisoned = true;
            throw Errors.Fail(ErrorCode.RecoveryRequired, SnapshotInstallInterrupted, original);
        }

        if (retiredTree is not null)
        {
            DeleteRetiredTree(retiredTree);
        }
    }

    private static global::ManagedCode.Communication.Result<string?> Prepare(ZoneTreeStoreRuntime runtime, bool replaceTree)
    {
        if (replaceTree)
        {
            runtime.PointCache?.Clear();
        }

        runtime.Identity = runtime.Identity with
        {
            FormatVersion = CurrentDataEpoch,
            ReadGeneration = replaceTree ? checked(runtime.Identity.ReadGeneration + 1) : runtime.Identity.ReadGeneration
        };
        ZoneTreeIdentityFile.Write(Path.Combine(runtime.Options.Directory, IdentityFileName), runtime.Identity, runtime.Options.IdentityBufferBytes);
        runtime.Journal.Flush(true);
        if (!replaceTree)
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(Prepare));
        }

        ZoneTreeStoreHandleDisposal.RetireMaintainer(runtime);
        ZoneTreeStoreHandleDisposal.RetireTree(runtime);
        var retiredTree = Path.Combine(runtime.Options.Directory, RetiredTreePrefix + Guid.NewGuid().ToString(GuidFormat));
        Directory.Move(Path.Combine(runtime.Options.Directory, TreeDirectoryName), retiredTree);
        return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(retiredTree)));
    }

    private static void Swap(ZoneTreeStoreRuntime runtime, string temporary, long cut, bool replaceTree)
    {
        runtime.Options.FaultObserver?.Invoke(CommitStage.InstallPrepared, cut, 0);
        runtime.Journal.Dispose();
        File.Move(temporary, Path.Combine(runtime.Options.Directory, JournalFileName), true);
        runtime.Options.FaultObserver?.Invoke(CommitStage.JournalSwapped, cut, 0);
        runtime.Journal = ZoneTreeStoreFiles.OpenJournal(runtime.Options);
        if (replaceTree)
        {
            runtime.Tree = ZoneTreeTreeFactory.Open(runtime.Options);
            runtime.SetPosition(0);
            ZoneTreeJournalRecovery.Recover(runtime);
            runtime.Maintenance = new(runtime.Tree, runtime.Options.MaintenanceExecution, TimeProvider.System);
            return;
        }

        runtime.Journal.Position = runtime.Journal.Length;
        runtime.SetPosition(cut);
    }

    private static void DeleteRetiredTree(string retiredTree)
    {
        try
        {
            Directory.Delete(retiredTree, true);
        }
        catch (IOException)
        {
            // The new journal is authoritative; derived files can wait for handle cleanup.
        }
    }
}
