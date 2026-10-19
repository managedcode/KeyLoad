using KeyLoad.Core.Features.CommandOutcomes.Identity;
using KeyLoad.Core.Features.RuntimeJournaling.Identity;

namespace KeyLoad.Clustering;

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
        if (request.Purpose == GrainContracts.RequestPurpose)
        {
            if (journalRead || journalCommand || protectedPrincipal)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
            }
            return;
        }
        if (request.Purpose != GrainContracts.RuntimeJournalPurpose
            || !(journalRead && protectedPrincipal || journalCommand))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
