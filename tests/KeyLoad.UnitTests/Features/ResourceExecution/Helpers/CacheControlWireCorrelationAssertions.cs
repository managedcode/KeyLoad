using KeyLoad.Clustering.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CacheControlWireCorrelationAssertions
{
    private const string CorrelationFailureMessage = "The valid request did not produce its exact correlation digest.";

    internal static CacheReplyCorrelation Correlation(ICacheControlRequest request)
    {
        var created = CacheControlCorrelation.TryCreate(request, out var correlation);
        if (!created || correlation is null)
        {
            throw new InvalidOperationException(CorrelationFailureMessage);
        }

        return correlation;
    }

    internal static CacheControlDigest TryDigest(ICacheControlRequest request)
    {
        var created = CacheControlCorrelation.TryCreate(request, out var correlation);
        if (!created || correlation is null)
        {
            throw new InvalidOperationException(CorrelationFailureMessage);
        }

        return correlation.SignedRequestDigest;
    }
}
