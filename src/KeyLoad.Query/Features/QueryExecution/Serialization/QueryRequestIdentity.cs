using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class QueryRequestIdentity
{
    internal static string Hash(AstQueryRequest request) => JsonData.Fingerprint(new
    {
        request.Partition,
        Query = request.Query with { Explain = false },
        request.Parameters,
        request.AllowFullScan,
        request.AstVersion
    });
}
