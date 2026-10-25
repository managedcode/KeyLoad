using KeyLoad.Core;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

public sealed partial class SearchEngine
{
    private readonly IQueryReadCutObserver? readCutObserver;

    internal SearchEngine(DatabaseEngine database, IOptions<QueryExecutionOptions> options,
        ITextProjection? textProjection, IAnnProjection? annProjection, IQueryReadCutObserver? readCutObserver)
        : this(database, options, textProjection)
    {
        this.annProjection = annProjection;
        this.readCutObserver = readCutObserver;
    }

    internal global::ManagedCode.Communication.Result<WaitForIndexReadCutObservation?> CaptureWaitForIndexReadCut(PrincipalRecord principal,
        ResourceDefinition resource, WaitForIndexRequest request, WaitForIndexResult result, ReadExecutionBudget budget)
        => readCutObserver is null ? global::ManagedCode.Communication.Result<WaitForIndexReadCutObservation?>.FailNotFound(nameof(CaptureWaitForIndexReadCut))
            : global::ManagedCode.Communication.Result<WaitForIndexReadCutObservation?>.Succeed(((WaitForIndexReadCutObservation?)(WaitForIndexReadCutObservationDispatch.Capture(database, principal, resource, request, result, budget))));

    internal void PublishWaitForIndexReadCut(WaitForIndexReadCutObservation? observation, ReadExecutionBudget budget)
        => WaitForIndexReadCutObservationDispatch.Observe(readCutObserver, observation, budget);
}
