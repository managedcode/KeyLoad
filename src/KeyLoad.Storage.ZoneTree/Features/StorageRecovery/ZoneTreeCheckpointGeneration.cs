using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCheckpointGeneration
{
    internal static void Replace(ZoneTreeStoreRuntime runtime, string temporary, long cut, bool replaceTree)
    {
        string? retiredTree;
        try
        {
            retiredTree = Prepare(runtime, replaceTree);
            Swap(runtime, temporary, cut, replaceTree);
        }
        catch (Exception)
        {
            runtime.Poisoned = true;
            throw Errors.Fail(ErrorCode.RecoveryRequired, SnapshotInstallInterrupted);
        }

        if (retiredTree is not null)
        {
            DeleteRetiredTree(retiredTree);
        }
    }

    private static string? Prepare(ZoneTreeStoreRuntime runtime, bool replaceTree)
    {
        runtime.Identity = runtime.Identity with
        {
            FormatVersion = BinaryJournalIdentityVersion,
            ReadGeneration = replaceTree ? checked(runtime.Identity.ReadGeneration + 1) : runtime.Identity.ReadGeneration
        };
        ZoneTreeIdentityFile.Write(Path.Combine(runtime.Options.Directory, IdentityFileName), runtime.Identity);
        runtime.Journal.Flush(true);
        if (!replaceTree)
        {
            return null;
        }

        runtime.Maintainer.Dispose();
        runtime.Tree.Dispose();
        var retiredTree = Path.Combine(runtime.Options.Directory, RetiredTreePrefix + Guid.NewGuid().ToString(GuidFormat));
        Directory.Move(Path.Combine(runtime.Options.Directory, TreeDirectoryName), retiredTree);
        return retiredTree;
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
            runtime.Maintainer = runtime.Tree.CreateMaintainer();
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
