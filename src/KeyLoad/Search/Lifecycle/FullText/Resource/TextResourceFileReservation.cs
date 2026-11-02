namespace KeyLoad.Server.Search;

internal sealed class TextResourceFileReservation(TextResourceOwnership owner,
    object /* KLD0041: One reservation ledger preserves string value equality for canonical paths and reference identity for TextOpenFileGroup instances; both keys are existing references. */ key, string path, long targetLength, Func<long> observeLength)
{
    private readonly Lock gate = new();
    private bool completed;
    internal object /* KLD0041: One reservation ledger preserves string value equality for canonical paths and reference identity for TextOpenFileGroup instances; both keys are existing references. */ Key { get; } = key;
    internal string Path { get; } = path;
    internal long TargetLength { get; private set; } = targetLength;
    internal long ObserveLength() => observeLength();

    internal void CompleteAfterJoinedWrite()
    {
        lock (gate)
        {
            if (completed)
            {
                throw TextErrors.Ownership();
            }
            // A joined failed write may leave a genuine shorter partial file.
            TargetLength = ObserveLength();
            owner.ReleaseFile(this);
            completed = true;
        }
    }
}
