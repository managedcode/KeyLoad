using KeyLoad.Clustering;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Server;

internal static class NativeDiscoveryCapabilityOmission
{
    internal static byte[] Omit(ReadOnlyMemory<byte> original, SiloStorageIdentity expected)
    {
        if (original.IsEmpty || original.Length > ReplicaTransportProtocol.MaximumDiscoveryBytes
            || expected.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal)
        {
            throw NativeDiscoveryCapabilityField.Invalid();
        }
        RequireOriginal(expected, NativeSerialization.Deserialize<SiloStorageIdentity>(original.Span));
        var context = NativeSerializerProviders.Get(typeof(SiloStorageIdentity));
        NativeDiscoveryCapabilityField.Span field;
        using (var session = context.Sessions.GetSession())
        {
            field = NativeDiscoveryCapabilityField.Find(original.Span, session)
            ?? throw NativeDiscoveryCapabilityField.Invalid();
        }
        var omitted = new byte[checked(original.Length - field.Length)];
        original.Span[..field.Start].CopyTo(omitted);
        original.Span[field.End..].CopyTo(omitted.AsSpan(field.Start));
        using (var session = context.Sessions.GetSession())
        {
            if (NativeDiscoveryCapabilityField.Find(omitted, session) is not null)
            {
                throw NativeDiscoveryCapabilityField.Invalid();
            }
        }
        var decoded = NativeSerialization.Deserialize<SiloStorageIdentity>(omitted);
        if (decoded.RuntimeJournalReaderContract != StoreReaderContract.Unspecified)
        {
            throw NativeDiscoveryCapabilityField.Invalid();
        }
        RequireOriginal(expected, decoded with
        {
            RuntimeJournalReaderContract = expected.RuntimeJournalReaderContract
        });
        return omitted;
    }

    private static void RequireOriginal(SiloStorageIdentity expected, SiloStorageIdentity actual)
    {
        if (actual.VoterId != expected.VoterId || actual.ClusterId != expected.ClusterId
            || actual.Incarnation != expected.Incarnation || actual.SiloAddress != expected.SiloAddress
            || actual.TransportReady != expected.TransportReady
            || actual.ApplicationRpcVersion != expected.ApplicationRpcVersion
            || actual.PeerEnvelopeVersion != expected.PeerEnvelopeVersion
            || actual.RuntimeJournalReaderContract != expected.RuntimeJournalReaderContract)
        {
            throw NativeDiscoveryCapabilityField.Invalid();
        }
    }
}
