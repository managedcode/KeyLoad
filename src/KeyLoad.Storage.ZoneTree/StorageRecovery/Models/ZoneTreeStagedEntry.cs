namespace KeyLoad.Storage.ZoneTree;

internal sealed record ZoneTreeStagedEntry(byte[] Key, byte[]? Value)
{
    internal static ZoneTreeStagedEntry High { get; } = new([], null) { IsHigh = true };
    internal bool IsHigh
    {
        get; private init;
    }
}
