using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal Guid? ReadOnlineTextPublicationHead(IKeyValueView view, PrincipalRecord principal,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        // Fresh administrator admission authorizes a new publication, not replay of another principal's result.
        RequireOnlineTextOriginalAuthority(view, principal, request);
        var current = view.GetRecord<OnlineTextCurrentPublication>(OnlineTextPublicationKeys.Current(request));
        if (current is null)
        { return null; }
        budget.ChargeBytes(NativeSerialization.Measure(current));
        if (current.FormatVersion != OnlineTextPublicationProtocol.CurrentFormat
            || current.CommandId == Guid.Empty || current.Consumer != request.Consumer
            || current.ConsumerGeneration != request.ConsumerGeneration
            || current.Authority.Collection != request.Collection || current.Authority.Field != request.Field
            || current.Authority.DataEpoch != Store.Identity.FormatVersion
            || current.PublishedCut.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication); }
        budget.Check();
        return current.CommandId;
    }
}
