using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes OnlineTextPublicationHead operations against the caller's database context.</summary>
public static class OnlineTextPublicationHead
{
    internal static (bool HasValue, Guid? Value) ReadOnlineTextPublicationHead(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        // Fresh administrator admission authorizes a new publication, not replay of another principal's result.
        engine.RequireOnlineTextOriginalAuthority(view, principal, request);
        var current = view.GetRecord<OnlineTextCurrentPublication>(OnlineTextPublicationKeys.Current(request));
        if (current is null)
        {
            return (false, default(Guid?));
        }

        budget.ChargeBytes(OrleansSerialization.Measure(current));
        if (current.FormatVersion != OnlineTextPublicationProtocol.CurrentFormat || current.CommandId == Guid.Empty || current.Consumer != request.Consumer || current.ConsumerGeneration != request.ConsumerGeneration || current.Authority.Collection != request.Collection || current.Authority.Field != request.Field || current.Authority.DataEpoch != engine.Store.Identity.FormatVersion || current.PublishedCut.Incarnation != engine.Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
        }

        budget.Check();
        return (true, ((Guid?)(current.CommandId)));
    }
}
