using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorSourcePhaseEncoding
{
    internal static EventVectorSourcePhase Encode(EventVectorSourcePhase original,
        EventVectorSourceMutation mutation, EventVectorAdmissionPolicy admission, ReadExecutionBudget? work = null)
    {
        var measured = NativeSerialization.Measure(mutation);
        admission.RequireEncodedBytes(measured);
        work?.ChargeBytes(measured);
        work?.ChargeBytes(SHA256.HashSizeInBytes);
        var body = NativeSerialization.Serialize(mutation);
        byte[]? digest = null;
        try
        {
            admission.RequireEncodedBytes(body.Length);
            digest = SHA256.HashData(body);
            var phase = original with
            {
                OriginalNativeBody = body,
                OriginalBodyDigest = digest
            };
            phase = phase with
            {
                PhaseCommandId = EventVectorSourcePhaseIds.For(phase, admission)
            };
            EventVectorSourceMutationValidation.ReadOriginal(phase, admission);
            return phase;
        }
        catch (Exception)
        {
            CryptographicOperations.ZeroMemory(body);
            if (digest is not null)
            {
                CryptographicOperations.ZeroMemory(digest);
            }
            throw;
        }
    }
}
