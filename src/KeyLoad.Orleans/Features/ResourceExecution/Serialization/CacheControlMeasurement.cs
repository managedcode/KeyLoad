using System.Text;

namespace KeyLoad.Orleans.Features.ResourceExecution;

internal readonly record struct CacheControlMeasurement(int CompleteBytes, int SigningBytes, int CorrelationBytes)
{
    internal const int MaximumBytes = 64 * 1024;

    internal static bool TryGet(ICacheControlMessage? message, out CacheControlMeasurement result)
    {
        result = default;
        if (!CacheControlShape.Valid(message))
        {
            return false;
        }

        try
        {
            var complete = new CacheControlWriter(default, measureOnly: true);
            CacheControlMessageEncoding.Object(ref complete, message!, includeMac: true);
            var signing = new CacheControlWriter(default, measureOnly: true);
            CacheControlMessageEncoding.SigningPrefix(ref signing, message!);
            CacheControlMessageEncoding.Object(ref signing, message!, includeMac: false);
            var correlationBytes = message is ICacheControlRequest
                ? checked(sizeof(uint) + CacheControlValidation.StrictUtf8.GetByteCount(CacheControlNames.CorrelationPurpose)
                    + sizeof(byte) + complete.Position)
                : 0;
            if (complete.Position > MaximumBytes || signing.Position > MaximumBytes || correlationBytes > MaximumBytes)
            {
                return false;
            }

            result = new(complete.Position, signing.Position, correlationBytes);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
