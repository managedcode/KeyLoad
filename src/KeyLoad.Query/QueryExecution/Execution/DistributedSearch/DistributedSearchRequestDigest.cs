using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchRequestDigest
{
    internal static string Create<T>(T value, ReadExecutionBudget budget)
    {
        budget.Check();
        budget.ChargeBytes(NativeSerialization.Measure(value));
        var bytes = NativeSerialization.Serialize(value);
        budget.Check();
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
