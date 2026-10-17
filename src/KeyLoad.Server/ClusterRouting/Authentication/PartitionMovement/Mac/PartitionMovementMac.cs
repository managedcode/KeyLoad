using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementMac
{
    private static readonly byte[] ControlledLifetimeDomain = Encoding.ASCII.GetBytes(PartitionControlLifetimeTransportProtocol.RequestPurpose);

    internal string SignControlledLifetime(ReadOnlySpan<byte> originalBody)
        => Convert.ToHexStringLower(DigestDomain(originalBody, ControlledLifetimeDomain));

    internal bool VerifyControlledLifetime(ReadOnlySpan<byte> originalBody, string signature)
        => PartitionMovementSignatureVerification.Verify(this, originalBody, signature, ControlledLifetimeDomain);
}
