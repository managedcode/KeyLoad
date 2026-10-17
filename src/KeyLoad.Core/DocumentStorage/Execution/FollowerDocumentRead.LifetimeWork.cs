using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal FollowerDocumentLifetimeSnapshot CaptureFollowerDocumentBound(string principalId,
        ReadFollowerDocumentRequestV1 request, PhysicalShardRecord owner, string replicaId, long term,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        ValidateFollowerReadRequest(request);
        return Store.Read(raw =>
        {
            budget.Check();
            var view = budget.CreateView(raw);
            var captured = CaptureFollowerDocumentAtCut(view, principalId, request, owner,
                replicaId, term, budget.Cancellation);
            var coverage = budget.CaptureResourceLifetimes(this, view, request.Reference.Partition, [captured.Resource]);
            budget.Check();
            return new FollowerDocumentLifetimeSnapshot(captured.Snapshot, coverage.Lifetimes.Single());
        });
    }

    internal FollowerDocumentReadResultV1 CompleteFollowerDocumentBound(string principalId,
        FollowerDocumentReadCapability capability, FollowerDocumentLifetimeSnapshot snapshot,
        string replicaId, long authorityTerm, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(budget);
        budget.RequireResourceLifetimeBinding(binding);
        var result = Store.Read(raw =>
        {
            budget.Check();
            var view = budget.CreateView(raw);
            return CompleteFollowerDocumentAtCut(view, principalId, capability, snapshot.Original,
                replicaId, authorityTerm, budget.Cancellation, resource =>
                {
                    var coverage = budget.CaptureResourceLifetimes(this, view,
                        capability.Request.Reference.Partition, [resource]);
                    if (coverage.Lifetimes.Single() != snapshot.Lifetime)
                    {
                        throw Errors.Fail(ErrorCode.OwnershipLost, FollowerReadOwnerChanged);
                    }
                });
        });
        budget.CheckResult(result);
        return result;
    }
}
