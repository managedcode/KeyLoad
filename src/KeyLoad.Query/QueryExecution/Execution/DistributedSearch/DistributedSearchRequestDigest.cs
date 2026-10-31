using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchRequestDigest
{
    internal static string Create<T>(T value, ReadExecutionBudget budget)
    {
        budget.Check();
        var bytes = OrleansSerialization.Serialize(value);
        budget.Check();
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
