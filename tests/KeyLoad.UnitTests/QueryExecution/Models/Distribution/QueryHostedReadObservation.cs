using KeyLoad.Clustering;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryHostedReadObservation(ZoneTreeStore expectedStore)
{
    internal ZoneTreeStore Store { get; } = expectedStore;
    internal long BeforeBytes { get; } = expectedStore.GetReadDiagnostics().RangeExaminedBytes;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal GrainRequestProbeIdentity Identity { get; set; }
    internal GrainId GrainId { get; set; }
    internal ActivationId ActivationId { get; set; }
    internal Guid NodeId { get; set; }
    internal long ObservedBytes { get; set; }
}
