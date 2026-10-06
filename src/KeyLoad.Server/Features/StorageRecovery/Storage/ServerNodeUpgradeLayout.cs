namespace KeyLoad.Server;

internal static class ServerNodeUpgradeLayout
{
    internal static void VerifySource(ServerNodeUpgradeInventory inventory)
    {
        const char SlashCharacter = '/';

        foreach (var entry in inventory.Entries)
        {
            if (!entry.Path.Contains(SlashCharacter, StringComparison.Ordinal))
            { RequireRoot(entry, allowReceipts: false, allowInputs: false); }
            ServerNodeUpgradeSourceLayout.Verify(entry);
            RefuseIncoming(entry);
        }
    }

    internal static void VerifyTarget(ServerNodeUpgradeInventory inventory, bool allowInputs)
    {
        const char SlashCharacter = '/';

        foreach (var entry in inventory.Entries)
        {
            if (!entry.Path.Contains(SlashCharacter, StringComparison.Ordinal))
            { RequireRoot(entry, allowReceipts: true, allowInputs); }
            ServerNodeUpgradeSourceLayout.Verify(entry, allowNestedReceipt: allowInputs);
            RefuseIncoming(entry);
        }
    }

    private static void RequireRoot(ServerNodeUpgradeEntry entry, bool allowReceipts, bool allowInputs)
    {
        var allowedDirectory = entry.Path is ServerNodeUpgradeProtocol.Canonical or ServerNodeUpgradeProtocol.Replica
            or ServerNodeUpgradeProtocol.Snapshots or ServerNodeUpgradeProtocol.Backups or ServerNodeUpgradeProtocol.SearchIndexes;
        allowedDirectory |= allowInputs && entry.Path is ServerNodeUpgradeProtocol.Inputs or ServerNodeUpgradeProtocol.PreparedImages or ServerNodeUpgradeProtocol.CanonicalUpgradeDirectory or ServerNodeUpgradeProtocol.ReplicaUpgradeDirectory;
        var allowedFile = entry.Path == ServerNodeUpgradeProtocol.NodeOwner || allowReceipts
            && entry.Path is ServerNodeUpgradeProtocol.OwnerReceipt or ServerNodeUpgradeProtocol.PreparedReceipt or ServerNodeUpgradeProtocol.ProgressReceipt;
        if (entry.Directory ? !allowedDirectory : !allowedFile)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }

    private static void RefuseIncoming(ServerNodeUpgradeEntry entry)
    {
        const int PathSecondIndex = 1;
        const char SlashCharacter = '/';
        const string SnapshotFileExtension = ".snapshot";
        const string CompactIdentityFormat = "N";

        if (!entry.Path.StartsWith(ServerNodeUpgradeProtocol.Snapshots + ServerNodeUpgradeProtocol.PathSeparator, StringComparison.Ordinal))
        { return; }
        var name = entry.Path[(ServerNodeUpgradeProtocol.Snapshots.Length + PathSecondIndex)..];
        if (name is ServerNodeUpgradeProtocol.IncomingMetadata or ServerNodeUpgradeProtocol.IncomingSnapshot or ServerNodeUpgradeProtocol.IncomingMetadataTemporary or ServerNodeUpgradeProtocol.IncomingSnapshotTemporary
            || name.EndsWith(ServerNodeUpgradeProtocol.SnapshotTemporarySuffix, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, ServerNodeUpgradeProtocol.Pending); }
        if (entry.Directory || name.Contains(SlashCharacter, StringComparison.Ordinal)
            || !name.EndsWith(ServerNodeUpgradeProtocol.SnapshotSuffix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name[..^SnapshotFileExtension.Length], CompactIdentityFormat, out _))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }
}
