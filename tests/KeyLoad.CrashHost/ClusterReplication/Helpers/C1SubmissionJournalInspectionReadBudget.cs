namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal sealed class C1SubmissionJournalInspectionReadBudget(CancellationToken cancellationToken)
{
    private const int NoBytes = 0;
    private long examined;
    internal void Charge(long bytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes < NoBytes || bytes > C1SubmissionJournalInspectionProtocol.MaximumNativeRecordBytes
            || bytes > C1SubmissionJournalInspectionProtocol.MaximumReadBytes - examined)
        {
            throw C1SubmissionJournalInspectionValidation.Invalid();
        }
        examined += bytes;
    }
}
