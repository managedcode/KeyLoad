using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes OnlineTextImplicitCurrent operations against the caller's database context.</summary>
public static class OnlineTextImplicitCurrent
{
    internal static (bool HasValue, OnlineTextCurrentPublication? Value) ReadOnlineTextImplicitCurrent(this DatabaseEngine engine, IKeyValueView raw, PrincipalRecord principal, PartitionRef partition, string collection, string field, Guid nodeId, ReadExecutionBudget budget)
    {
        budget.Check();
        if (!principal.ClusterAdministrator)
        {
            return (false, default(OnlineTextCurrentPublication?));
        }

        OnlineTextCurrentPublication? selected = null;
        var view = budget.CreateView(raw);
        _ = budget.VisitAll(raw, OnlineTextPublicationKeys.Prefix(partition, collection, field), (key, value) =>
        {
            budget.ChargeBytes(value.Length);
            var current = OrleansSerialization.Deserialize<OnlineTextCurrentPublication>(value);
            var request = new OnlineTextIndexMaintenanceRequest(current.CommandId, current.Consumer, current.Authority.Collection, current.Authority.Field, current.ConsumerGeneration, current.PublishedCut.NodeId, current.Authority.Placement);
            if (!key.SequenceEqual(OnlineTextPublicationKeys.Current(request)) || current.Consumer.Partition != partition || current.Authority.Collection != collection || current.Authority.Field != field)
            {
                throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
            }

            if (current.PrincipalId != principal.Id || current.PublishedCut.NodeId != nodeId)
            {
                return true;
            }

            var configured = ProjectionConsumerIdentity.ProjectionConsumer(view, current.Consumer);
            if (configured.Released || configured.Definition.IndexGeneration != current.ConsumerGeneration)
            {
                return true;
            }

            var actual = (engine.ReadOnlineTextCurrentPublication(view, principal, request, budget)).Value ?? throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.MissingAuthority);
            if (selected is not null)
            {
                throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.InvalidPublication);
            }

            selected = actual;
            return true;
        });

        return ((OnlineTextCurrentPublication?)(selected)) is { } outcomeValue2 ? (true, outcomeValue2) : (false, default(OnlineTextCurrentPublication?));
    }
}
