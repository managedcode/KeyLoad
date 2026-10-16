using System.Text;

namespace KeyLoad.Core.Features.AnalyticsStore;

internal static class AnalyticsStoreDimensionValues
{
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static int Require(string value)
    {
        try
        {
            return StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException error)
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid, error);
        }
    }
}
