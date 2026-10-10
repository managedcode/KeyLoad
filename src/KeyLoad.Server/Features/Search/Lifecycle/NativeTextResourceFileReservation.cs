namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextResourceFileReservation(NativeTextResourceOwnership owner,
    object key, string path, long targetLength, Func<long> observeLength)
{
    private readonly Lock gate = new();
    private bool completed;
    internal object Key { get; } = key;
    internal string Path { get; } = path;
    internal long TargetLength { get; private set; } = targetLength;
    internal long ObserveLength() => observeLength();

    internal void CompleteAfterJoinedWrite()
    {
        lock (gate)
        {
            if (completed)
            { throw NativeTextErrors.Ownership(); }
            // A joined failed write may leave a genuine shorter partial file.
            TargetLength = ObserveLength();
            owner.ReleaseFile(this);
            completed = true;
        }
    }
}
