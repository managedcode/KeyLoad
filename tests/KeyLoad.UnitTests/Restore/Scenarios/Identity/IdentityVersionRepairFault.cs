using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class IdentityVersionRepairFault
{
    internal static byte[] Create(byte[] original, bool codecVersion)
    {
        var envelope = ZoneTreeMetadataBinary.Read<ZoneTreeIdentityEnvelope>(original,
            ZoneTreeMetadataBinary.IdentityMagic, ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Checksum))
        {
            throw new InvalidOperationException("The fixture original identity checksum is invalid.");
        }
        var identity = NativeSerialization.Deserialize<StoreIdentity>(envelope.Payload);
        var changed = codecVersion
            ? identity with
            {
                KeyCodecVersion = checked(identity.KeyCodecVersion + 1)
            }
            : identity with
            {
                FormatVersion = checked(identity.FormatVersion + 1)
            };
        var payload = NativeSerialization.Serialize(changed);
        return ZoneTreeMetadataBinary.Write(new ZoneTreeIdentityEnvelope(payload, SHA256.HashData(payload)),
            ZoneTreeMetadataBinary.IdentityMagic);
    }
}
