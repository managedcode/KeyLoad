using System.Buffers.Text;
using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class GrainRequestTokenBounds
{
    private const int SignatureBytes = 32;
    private const int SeparatorCharacters = 1;

    internal static int MaximumCharacters(DatabaseEngine database)
    {
        var envelopeBytes = checked(database.Limits.MaxBatchBytes + GrainRoutingProtocol.EnvelopeMetadataBytes);
        return checked(Base64Url.GetEncodedLength(envelopeBytes)
            + GrainNativeContracts.SignedTokenPrefix.Length + SeparatorCharacters + Base64Url.GetEncodedLength(SignatureBytes));
    }
}
