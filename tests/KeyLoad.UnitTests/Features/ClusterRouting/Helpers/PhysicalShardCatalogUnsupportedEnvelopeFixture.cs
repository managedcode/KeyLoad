using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Authors one unsupported native envelope through the existing generated serializer provider.</summary>
internal static class PhysicalShardCatalogUnsupportedEnvelopeFixture
{
    private const uint UnsupportedVersion = NativePayloadVersion.Current + 1;

    internal static byte[] Encode(PhysicalShardCatalog catalog)
    {
        var context = NativeSerializerProviders.Get(typeof(PhysicalShardCatalog));
        return context.Serializer.SerializeToArray(new NativePayload
        { Version = UnsupportedVersion, Value = catalog });
    }
}
