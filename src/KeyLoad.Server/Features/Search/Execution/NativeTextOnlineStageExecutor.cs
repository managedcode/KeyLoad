using KeyLoad.Core;
using KeyLoad.Orleans.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineStageExecutor
{
    internal static OnlineTextCapabilityResult Execute(DatabaseEngine database, NativeTextOnlineRoot root, NativeTextResourceOwnership resources,
        ServerRuntimeOptions options, TimeProvider clock, Action<NativeTextOnlineSession> requireCurrentAuthority,
        PrincipalRecord principal, NativeTextOnlineSession session,
        OnlineTextCapabilityRequest request)
    {
        switch (request.Kind)
        {
            case OnlineTextCapabilityKind.ResolveOriginal:
                var original = database.ReadOnlineTextOriginalOutcome(principal.Id, request.Request, session.Budget);
                return NativeTextOnlineCapabilityResult.Create(session, original?.Get<OnlineTextIndexMaintenanceResult>());
            case OnlineTextCapabilityKind.Capture:
                NativeTextOnlineCapture.Execute(database, session, resources, clock);
                break;
            case OnlineTextCapabilityKind.Seed:
                NativeTextOnlineSeed.Execute(database, session, root, resources, options);
                requireCurrentAuthority(session);
                break;
            case OnlineTextCapabilityKind.PreparePage:
                NativeTextOnlinePagePreparation.Execute(database, session,
                    request.Page ?? throw NativeTextErrors.Corrupt(),
                    request.CheckpointIntent ?? throw NativeTextErrors.Corrupt(), resources, options);
                break;
            case OnlineTextCapabilityKind.ApplyIntent:
                requireCurrentAuthority(session);
                NativeTextOnlineIntentApplication.Execute(database, session, root, resources, options);
                break;
            case OnlineTextCapabilityKind.Validate:
                NativeTextOnlineValidation.Execute(database, session, options);
                break;
            case OnlineTextCapabilityKind.IssuePublication:
                NativeTextOnlinePublication.Issue(database, session, options, clock);
                break;
            default:
                throw NativeTextErrors.Mismatch();
        }
        return NativeTextOnlineCapabilityResult.Create(session);
    }
}
