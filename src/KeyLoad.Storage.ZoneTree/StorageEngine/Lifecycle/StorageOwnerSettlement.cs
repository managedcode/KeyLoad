namespace KeyLoad.Storage.ZoneTree;

/// <summary>Records only this actual execution owner attempt and its original physical release.</summary>
internal sealed class StorageOwnerSettlement
{
    private const string AttemptAlreadyStarted = "A execution ownership settlement belongs to one original acquisition attempt.";
    private bool opened;
    private bool released = true;
    private bool constructorPending;

    internal bool IsReleased => released && !constructorPending;

    internal void BeginOpen()
    {
        if (opened)
        {
            throw new InvalidOperationException(AttemptAlreadyStarted);
        }
        opened = true;
        released = false;
    }

    internal void BeginConstructor() => constructorPending = true;

    internal void ConstructorReturned() => constructorPending = false;

    internal void ReleaseReturned() => released = true;
}
