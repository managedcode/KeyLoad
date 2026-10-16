using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourceRetirementRf3Signature
{
    private const int SignatureCharacters = 64;

    internal static void Verify(HttpResponseMessage response, NativeResourceRetirementDiagnosticRequest request,
        byte[] payload, ReplicaSiloDiscovery owner, ReadOnlyMemory<byte> secret)
    {
        if (!response.Headers.TryGetValues(NativeResourceRetirementDiagnosticProtocol.SignatureHeader, out var values))
        {
            throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        }
        using var signatures = values.GetEnumerator();
        if (!signatures.MoveNext())
        {
            throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        }
        var signature = signatures.Current;
        if (signatures.MoveNext() || signature.Length != SignatureCharacters)
        {
            throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        }
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature);
        }
        catch (FormatException) { throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid); }
        using var mac = new ReplicaMessageMac(secret, owner.ClusterId);
        var expected = mac.Discovery(owner.Incarnation, owner.VoterId, request.Nonce,
            NativeResourceRetirementDiagnosticProtocol.Frame(payload, Options.Create(new DatabaseLimits())));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            {
                throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(supplied);
        }
    }
}
