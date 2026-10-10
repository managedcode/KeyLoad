using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal OnlineTextCurrentPublication? ReadOnlineTextImplicitCurrent(IKeyValueView raw,
        PrincipalRecord principal, PartitionRef partition, string collection, string field,
        Guid nodeId, ReadExecutionBudget budget)
    {
        budget.Check();
        if (!principal.ClusterAdministrator)
        { return null; }
        OnlineTextCurrentPublication? selected = null;
        var view = budget.CreateView(raw);
        var scan = budget.VisitRange(raw, OnlineTextPublicationKeys.Prefix(partition, collection, field),
            Limits.MaxScanRecords, (key, value) =>
            {
                budget.ChargeBytes(value.Length);
                var current = NativeSerialization.Deserialize<OnlineTextCurrentPublication>(value);
                var request = new OnlineTextIndexMaintenanceRequest(current.CommandId, current.Consumer,
                    current.Authority.Collection, current.Authority.Field, current.ConsumerGeneration,
                    current.PublishedCut.NodeId, current.Authority.Placement);
                if (!key.SequenceEqual(OnlineTextPublicationKeys.Current(request))
                    || current.Consumer.Partition != partition || current.Authority.Collection != collection
                    || current.Authority.Field != field)
                { throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication); }
                if (current.PrincipalId != principal.Id || current.PublishedCut.NodeId != nodeId)
                { return true; }
                var configured = ProjectionConsumer(view, current.Consumer);
                if (configured.Released || configured.Definition.IndexGeneration != current.ConsumerGeneration)
                { return true; }
                var actual = ReadOnlineTextCurrentPublication(view, principal, request, budget)
                    ?? throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.MissingAuthority);
                if (selected is not null)
                { throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.InvalidPublication); }
                selected = actual;
                return true;
            });
        if (scan.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, OnlineTextPublicationProtocol.InvalidPublication); }
        return selected;
    }
}
