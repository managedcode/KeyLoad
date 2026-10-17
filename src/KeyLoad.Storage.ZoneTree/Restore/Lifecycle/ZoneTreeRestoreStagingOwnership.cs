namespace KeyLoad.Storage.ZoneTree;

/// <summary>A failed acquisition or close cannot authorize recursive staging removal.</summary>
internal sealed class ZoneTreeRestoreStagingOwnership
{
    internal bool IsSettled { get; private set; } = true;

    internal void BeginOpen() => IsSettled = false;

    internal void CompleteClose() => IsSettled = true;
}
