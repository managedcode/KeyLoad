using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal sealed class C1SubmissionJournalInspectionStore(IAtomicStore original,
    C1SubmissionJournalInspectionReadBudget budget, CancellationToken lifetime) : IAtomicStore
{
    public StoreIdentity Identity => original.Identity;
    public long Position => original.Position;
    public T Read<T>(Func<IKeyValueView, T> read)
    {
        lifetime.ThrowIfCancellationRequested();
        return original.Read(view => read(new C1SubmissionJournalInspectionView(view, budget, lifetime)));
    }
    public T Commit<T>(Func<IAtomicTransaction, long, T> compile)
        => throw C1SubmissionJournalInspectionValidation.Invalid();
    public void SetDispatchPaused(bool paused) => throw C1SubmissionJournalInspectionValidation.Invalid();
    public long CreateBackup(string directory) => throw C1SubmissionJournalInspectionValidation.Invalid();
    public StorageSnapshot CreateSnapshot(string path, long? expectedAppliedPosition = null)
        => throw C1SubmissionJournalInspectionValidation.Invalid();
    public StorageSnapshot VerifySnapshot(string path) => throw C1SubmissionJournalInspectionValidation.Invalid();
    public StorageSnapshot InstallSnapshot(string path, long expectedAppliedPosition)
        => throw C1SubmissionJournalInspectionValidation.Invalid();
    public StorageSnapshot Compact() => throw C1SubmissionJournalInspectionValidation.Invalid();
    public void Dispose() { }
}
