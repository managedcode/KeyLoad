namespace KeyLoad.Server;

internal static class ServerNodeUpgradeSourceLayout
{
    private const string TreeDirectory = "tree";
    private const string NestedReceipt = "format-upgrade.bin";

    internal static void Verify(ServerNodeUpgradeEntry entry, bool allowNestedReceipt = false)
    {
        const char SlashCharacter = '/';
        const int SeparatorValidationBoundary = 0;
        const int PathSecondIndex = 1;
        const int EmptyEntryLength = 0;

        var separator = entry.Path.IndexOf(SlashCharacter, StringComparison.Ordinal);
        if (separator < SeparatorValidationBoundary)
        { return; }
        var store = entry.Path[..separator];
        if (store is not (ServerNodeUpgradeProtocol.Canonical or ServerNodeUpgradeProtocol.Replica))
        { return; }
        var relative = entry.Path[(separator + PathSecondIndex)..];
        if (relative.StartsWith(TreeDirectory + ServerNodeUpgradeProtocol.PathSeparator, StringComparison.Ordinal)
            || relative == TreeDirectory && entry.Directory)
        { return; }
        var knownFile = relative is ServerNodeUpgradeProtocol.Identity or ServerNodeUpgradeProtocol.Journal
            || relative == ServerNodeUpgradeProtocol.StoreOwner && entry.Length == EmptyEntryLength
            || allowNestedReceipt && relative == NestedReceipt;
        if (entry.Directory || !knownFile)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }
}
