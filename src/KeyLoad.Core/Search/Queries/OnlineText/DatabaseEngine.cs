using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal OnlineTextCurrentPublication? ReadOnlineTextCurrentPublication(IKeyValueView view,
        PrincipalRecord principal, OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        RequireOnlineTextOriginalAuthority(view, principal, request);
        var current = view.GetRecord<OnlineTextCurrentPublication>(OnlineTextPublicationKeys.Current(request));
        if (current is null)
        {
            return null;
        }
        budget.ChargeBytes(OrleansSerialization.Measure(current));
        if (current.FormatVersion != OnlineTextPublicationProtocol.CurrentFormat
            || current.PrincipalId != principal.Id || current.Consumer != request.Consumer
            || current.ConsumerGeneration != request.ConsumerGeneration
            || current.Authority.Collection != request.Collection || current.Authority.Field != request.Field)
        {
            throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
        }
        var originalRequest = new OnlineTextIndexMaintenanceRequest(current.CommandId, current.Consumer,
            current.Authority.Collection, current.Authority.Field, current.ConsumerGeneration,
            current.PublishedCut.NodeId, current.Authority.Placement);
        RequireOnlineTextOriginalAuthority(view, principal, originalRequest);
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Consumer.Partition);
        var stored = CommandOutcomeKeyResolver.Select(view, principal.Id, current.CommandId, scope).Outcome
            ?? throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.MissingAuthority);
        ValidateOnlineTextStoredOutcome(view, principal, originalRequest, stored, budget);
        if (Errors.Code(stored.Result.Problem) is not null || stored.OnlineTextAuthority is null
            || current.ParentFingerprint != stored.OnlineTextAuthority.ParentFingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.MissingAuthority);
        }
        budget.ChargeBytes(OrleansSerialization.Measure(current.Authority));
        budget.ChargeBytes(OrleansSerialization.Measure(stored.OnlineTextAuthority));
        if (!OrleansSerialization.Serialize(current.Authority).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(stored.OnlineTextAuthority))
            || current.PublishedCut != stored.Result.Get<OnlineTextIndexMaintenanceResult>().PublishedCut)
        {
            throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.MissingAuthority);
        }
        budget.Check();
        return current;
    }
}
