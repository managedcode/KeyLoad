namespace KeyLoad.Server;

internal static class ServerNodeUpgradeLayout
{
    internal static void VerifySource(ServerNodeUpgradeInventory inventory)
    {
        foreach (var entry in inventory.Entries)
        {
            if (!entry.Path.Contains('/', StringComparison.Ordinal))
            { RequireRoot(entry, allowReceipts: false, allowInputs: false); }
            ServerNodeUpgradeSourceLayout.Verify(entry);
            RefuseIncoming(entry);
        }
    }

    internal static void VerifyTarget(ServerNodeUpgradeInventory inventory, bool allowInputs)
    {
        foreach (var entry in inventory.Entries)
        {
            if (!entry.Path.Contains('/', StringComparison.Ordinal))
            { RequireRoot(entry, allowReceipts: true, allowInputs); }
            ServerNodeUpgradeSourceLayout.Verify(entry, allowNestedReceipt: allowInputs);
            RefuseIncoming(entry);
        }
    }

    private static void RequireRoot(ServerNodeUpgradeEntry entry, bool allowReceipts, bool allowInputs)
    {
        var allowedDirectory = entry.Path is ServerNodeUpgradeProtocol.Canonical or ServerNodeUpgradeProtocol.Replica
            or ServerNodeUpgradeProtocol.Snapshots or ServerNodeUpgradeProtocol.Backups or ServerNodeUpgradeProtocol.SearchIndexes;
        allowedDirectory |= allowInputs && entry.Path is ServerNodeUpgradeProtocol.Inputs or ServerNodeUpgradeProtocol.PreparedImages or "database.upgrade" or "replica.upgrade";
        var allowedFile = entry.Path == ServerNodeUpgradeProtocol.NodeOwner || allowReceipts
            && entry.Path is ServerNodeUpgradeProtocol.OwnerReceipt or ServerNodeUpgradeProtocol.PreparedReceipt or ServerNodeUpgradeProtocol.ProgressReceipt;
        if (entry.Directory ? !allowedDirectory : !allowedFile)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }

    private static void RefuseIncoming(ServerNodeUpgradeEntry entry)
    {
        if (!entry.Path.StartsWith(ServerNodeUpgradeProtocol.Snapshots + "/", StringComparison.Ordinal))
        { return; }
        var name = entry.Path[(ServerNodeUpgradeProtocol.Snapshots.Length + 1)..];
        if (name is "incoming.json" or "incoming.snapshot" or "incoming.json.tmp" or "incoming.snapshot.tmp"
            || name.EndsWith(".snapshot.tmp", StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, ServerNodeUpgradeProtocol.Pending); }
        if (entry.Directory || name.Contains('/', StringComparison.Ordinal)
            || !name.EndsWith(".snapshot", StringComparison.Ordinal)
            || !Guid.TryParseExact(name[..^".snapshot".Length], "N", out _))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }
}
