using System.Buffers.Text;

namespace KeyLoad.Clustering;

internal static class GrainRequestTokenBounds
{
    private const int SignatureBytes = 32;
    private const int SeparatorCharacters = 1;

    internal static int MaximumCharacters(int maximumPayloadBytes)
    {
        var envelopeBytes = checked(maximumPayloadBytes + GrainRoutingProtocol.EnvelopeMetadataBytes);
        return checked(Base64Url.GetEncodedLength(envelopeBytes)
            + GrainContracts.SignedTokenPrefix.Length + SeparatorCharacters + Base64Url.GetEncodedLength(SignatureBytes));
    }
}
