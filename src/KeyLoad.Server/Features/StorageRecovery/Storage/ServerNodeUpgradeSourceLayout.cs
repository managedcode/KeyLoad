namespace KeyLoad.Server;

internal static class ServerNodeUpgradeSourceLayout
{
    private const string TreeDirectory = "tree";
    private const string NestedReceipt = "format-upgrade.bin";

    internal static void Verify(ServerNodeUpgradeEntry entry, bool allowNestedReceipt = false)
    {
        var separator = entry.Path.IndexOf('/', StringComparison.Ordinal);
        if (separator < 0)
        { return; }
        var store = entry.Path[..separator];
        if (store is not (ServerNodeUpgradeProtocol.Canonical or ServerNodeUpgradeProtocol.Replica))
        { return; }
        var relative = entry.Path[(separator + 1)..];
        if (relative.StartsWith(TreeDirectory + ServerNodeUpgradeProtocol.PathSeparator, StringComparison.Ordinal)
            || relative == TreeDirectory && entry.Directory)
        { return; }
        var knownFile = relative is ServerNodeUpgradeProtocol.Identity or ServerNodeUpgradeProtocol.Journal
            || relative == ServerNodeUpgradeProtocol.StoreOwner && entry.Length == 0
            || allowNestedReceipt && relative == NestedReceipt;
        if (entry.Directory || !knownFile)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }
}
