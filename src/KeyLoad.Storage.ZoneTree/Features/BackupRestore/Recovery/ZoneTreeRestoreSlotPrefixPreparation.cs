namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeRestoreSlotPrefixPreparation
{
    internal static void Require(string backup, string root, ZoneTreeStorageExecutionOptions policy,
        StoreIdentity original, ZoneTreeBackupRestoreManifest manifest, bool published,
        Action<NativeClusterRestoreStage>? observer, string identityFileName, string journalFileName, CancellationToken ct)
    {
        var identityPath = Path.Combine(root, identityFileName);
        var journal = manifest.Files.Single(file => file.Name == journalFileName);
        if (!File.Exists(identityPath))
        {
            if (published)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreeRestoreSlotDriver.Invalid);
            }
            ZoneTreeRestoreSlotPrefix.Continue(backup, root, journal, policy, ct);
            observer?.Invoke(NativeClusterRestoreStage.SourcePrefixPersisted);
            ZoneTreeIdentityFile.Write(identityPath, original, policy.IdentityBufferBytes);
        }
        ZoneTreeRestoreSlotPrefix.RequireOriginalPrefix(backup, root, journal, policy, ct);
    }
}
