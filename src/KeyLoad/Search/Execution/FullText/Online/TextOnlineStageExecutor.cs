using KeyLoad.Clustering.Features.Search;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class TextOnlineStageExecutor
{
    internal static OnlineTextCapabilityResult Execute(DatabaseEngine database, TextOnlineRoot root, TextResourceOwnership resources,
        ServerRuntimeOptions options, TimeProvider clock, Action<TextOnlineSession> requireCurrentAuthority,
        PrincipalRecord principal, TextOnlineSession session,
        OnlineTextCapabilityRequest request)
    {
        switch (request.Kind)
        {
            case OnlineTextCapabilityKind.ResolveOriginal:
                var original = database.ReadOnlineTextOriginalOutcome(principal.Id, request.Request, session.Budget);
                return TextOnlineCapabilityResult.Create(session, original?.Get<OnlineTextIndexMaintenanceResult>());
            case OnlineTextCapabilityKind.Capture:
                TextOnlineCapture.Execute(database, session, resources, clock);
                break;
            case OnlineTextCapabilityKind.Seed:
                TextOnlineSeed.Execute(database, session, root, resources, options);
                requireCurrentAuthority(session);
                break;
            case OnlineTextCapabilityKind.PreparePage:
                TextOnlinePagePreparation.Execute(database, session,
                    request.Page ?? throw TextErrors.Corrupt(),
                    request.CheckpointIntent ?? throw TextErrors.Corrupt(), resources, options);
                break;
            case OnlineTextCapabilityKind.ApplyIntent:
                requireCurrentAuthority(session);
                TextOnlineIntentApplication.Execute(database, session, root, resources, options);
                break;
            case OnlineTextCapabilityKind.Validate:
                TextOnlineValidation.Execute(database, session, options);
                break;
            case OnlineTextCapabilityKind.IssuePublication:
                TextOnlinePublication.Issue(database, session, options, clock);
                break;
            default:
                throw TextErrors.Mismatch();
        }
        return TextOnlineCapabilityResult.Create(session);
    }
}
