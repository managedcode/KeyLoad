namespace KeyLoad.Storage.ZoneTree;

/// <summary>Records only this actual native owner attempt and its original physical release.</summary>
internal sealed class NativeStorageOwnerSettlement
{
    private const string AttemptAlreadyStarted = "A native ownership settlement belongs to one original acquisition attempt.";
    private bool opened;
    private bool released = true;
    private bool nativeConstructorPending;

    internal bool IsReleased => released && !nativeConstructorPending;

    internal void BeginOpen()
    {
        if (opened)
        {
            throw new InvalidOperationException(AttemptAlreadyStarted);
        }
        opened = true;
        released = false;
    }

    internal void BeginNativeConstructor() => nativeConstructorPending = true;

    internal void NativeConstructorReturned() => nativeConstructorPending = false;

    internal void ReleaseReturned() => released = true;
}
