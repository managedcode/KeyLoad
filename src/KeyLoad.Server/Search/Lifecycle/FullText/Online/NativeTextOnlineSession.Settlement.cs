using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineSession
{
    internal void RegisterPublication(OnlineTextIndexMaintenanceResult expected)
    {
        Budget.Check();
        if (expectedPublication is not null || expected.CommandId != Request.CommandId)
        {
            throw NativeTextErrors.Mismatch();
        }
        Budget.ChargeBytes(NativeSerialization.Measure(expected));
        expectedPublication = NativeSerialization.Serialize(expected);
    }

    internal OnlineTextPublicationWork RequirePreparedPublication()
    {
        if (expectedPublication is null || Issued is null)
        {
            throw NativeTextErrors.Ownership();
        }
        return Publication;
    }

    internal async Task CloseCommandAdmissionAndJoinAsync()
    {
        await NativeTextOnlineSessionWork.JoinAsync(this, RequirePublicationReceipt).ConfigureAwait(false);
        commandsJoined = true;
    }

    private void RequirePublicationReceipt(OperationResult original)
        => JoinedPublicationReceipt = NativeTextOnlineSessionWork.RequireReceipt(original, ref expectedPublication, Request.CommandId);
}
