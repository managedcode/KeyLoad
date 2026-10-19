namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Retains before and observed execution range counters for one actually selected physical store.</summary>
internal sealed class QueryHostedPhysicalRead(QueryHostedStorageOwner owner)
{
    internal QueryHostedStorageOwner Owner { get; } = owner;
    internal long BeforeBytes { get; } = owner.Store.GetReadDiagnostics().RangeExaminedBytes;
    internal long ObservedBytes { get; private set; }
    internal bool HasProgress => Owner.Store.GetReadDiagnostics().RangeExaminedBytes > BeforeBytes;

    internal void CaptureProgress()
    {
        ObservedBytes = Owner.Store.GetReadDiagnostics().RangeExaminedBytes;
        if (ObservedBytes <= BeforeBytes)
        {
            throw new InvalidOperationException("The execution query reached statistics without reading canonical records.");
        }
    }
}
