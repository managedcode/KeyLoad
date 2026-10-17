using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Signature
{
    private const int SignatureCharacters = 64;
    private const string Invalid = "The actual native inventory signature failed verification.";

    internal static void Verify(HttpResponseMessage response, NativeResourceOwnerInventoryDiagnosticRequest request,
        byte[] payload, ReplicaSiloDiscovery owner, ReadOnlyMemory<byte> secret)
    {
        if (!response.Headers.TryGetValues(NativeResourceOwnerInventoryDiagnosticProtocol.SignatureHeader, out var values))
        {
            throw new InvalidDataException(Invalid);
        }
        using var signatures = values.GetEnumerator();
        if (!signatures.MoveNext())
        {
            throw new InvalidDataException(Invalid);
        }
        var signature = signatures.Current;
        if (signatures.MoveNext() || signature.Length != SignatureCharacters)
        {
            throw new InvalidDataException(Invalid);
        }
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature);
        }
        catch (FormatException) { throw new InvalidDataException(Invalid); }
        using var mac = new ReplicaMessageMac(secret, owner.ClusterId);
        var expected = mac.Discovery(owner.Incarnation, owner.VoterId, request.Nonce,
            NativeResourceOwnerInventoryDiagnosticProtocol.Frame(payload, Options.Create(new DatabaseLimits())));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            {
                throw new InvalidDataException(Invalid);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(supplied);
        }
    }
}
