using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorReceivingRead
{
    internal static async Task<T> ExecuteAsync<T, TRequest>(EventVectorNativeReceivingCall native,
        EventVectorRemoteCall call, PrincipalRecord principal, GrainReadKind kind, TRequest request,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var payload = EventVectorWorkEncoding.Encode(request, encoding, work);
        try
        {
            return await native.ReadAsync<T>(call, principal, kind, payload, work,
                originalCancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }
}
