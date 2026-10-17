using System.Text.Json;

namespace KeyLoad.Core;

internal static class EventVectorControlOperationIdentity
{
    internal static (Guid CommandId, string Metadata) Create(EventVectorControlPhase phase,
        string principalId, EventVectorAdmissionPolicy admission)
    {
        if (phase.Action == EventVectorControlAction.AdmitFirstIntent)
        {
            var first = EventVectorFirstIssuanceIds.Admit(phase.OriginalRequest, principalId, admission);
            return (EventVectorFirstIssuanceIds.For(first, admission),
                JsonSerializer.Serialize(first, JsonDefaults.Options));
        }
        var identity = EventVectorControlPhaseIds.Project(phase, principalId, admission);
        return (EventVectorControlPhaseIds.For(phase, principalId, identity.OriginalBodyDigest, admission),
            JsonSerializer.Serialize(identity, JsonDefaults.Options));
    }
}
