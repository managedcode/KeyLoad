using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Orleans;

internal static class RuntimeJournalRequestScope
{
    internal static bool Handles(GrainReadKind? kind)
        => kind is GrainReadKind.RuntimeJournalHeader or GrainReadKind.RuntimeJournalPage
            or GrainReadKind.RuntimeJournalCatalog;

    internal static void Validate(GrainRequestEnvelope request)
    {
        var journalRead = Handles(request.ReadKind);
        var journalCommand = request.CommandKind == OperationKind.RuntimeJournal;
        var protectedPrincipal = request.PrincipalId == RuntimeJournalIdentity.ProtectedPrincipalId;
        if (request.Purpose == GrainNativeContracts.RequestPurpose)
        {
            if (journalRead || journalCommand || protectedPrincipal)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
            }
            return;
        }
        if (request.Purpose != GrainNativeContracts.RuntimeJournalPurpose
            || !(journalRead && protectedPrincipal || journalCommand))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
