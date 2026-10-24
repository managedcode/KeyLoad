using KeyLoad.Clustering;
using ManagedCode.Communication;

namespace KeyLoad.Server.Search;

internal sealed partial class TextOnlineSession
{
    internal void RegisterPublication(OnlineTextIndexMaintenanceResult expected)
    {
        Budget.Check();
        if (expectedPublication is not null || expected.CommandId != Request.CommandId)
        {
            throw TextErrors.Mismatch();
        }
        Budget.ChargeBytes(OrleansSerialization.Measure(expected));
        expectedPublication = OrleansSerialization.Serialize(expected);
    }

    internal OnlineTextPublicationWork RequirePreparedPublication()
    {
        if (expectedPublication is null || Issued is null)
        {
            throw TextErrors.Ownership();
        }
        return Publication;
    }

    internal async Task CloseCommandAdmissionAndJoinAsync()
    {
        await TextOnlineSessionWork.JoinAsync(this, RequirePublicationReceipt);
        commandsJoined = true;
    }

    private void RequirePublicationReceipt(Result<object> original)
        => JoinedPublicationReceipt = TextOnlineSessionWork.RequireReceipt(original, ref expectedPublication, Request.CommandId);
}
