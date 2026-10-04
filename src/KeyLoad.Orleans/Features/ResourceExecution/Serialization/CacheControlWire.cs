namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlWire
{
    internal static bool TryEncodeSigned(ICacheControlMessage? message, out byte[] bytes)
        => TryEncode(message, signing: false, out bytes);

    internal static bool TryEncodeForSigning(ICacheControlMessage? message, out byte[] bytes)
        => TryEncode(message, signing: true, out bytes);

    private static bool TryEncode(ICacheControlMessage? message, bool signing, out byte[] bytes)
    {
        bytes = [];
        if (!CacheControlMeasurement.TryGet(message, out var measurement)
            || signing && CacheControlShape.IsFlat(message!))
        {
            return false;
        }

        var output = new byte[signing ? measurement.SigningBytes : measurement.CompleteBytes];
        var writer = new CacheControlWriter(output);
        if (signing)
        {
            CacheControlMessageEncoding.SigningPrefix(ref writer, message!);
        }

        CacheControlMessageEncoding.Object(ref writer, message!, includeMac: !signing);
        bytes = output;
        return true;
    }
}
