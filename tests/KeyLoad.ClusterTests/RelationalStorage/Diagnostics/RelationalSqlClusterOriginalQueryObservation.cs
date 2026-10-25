using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.ClusterTests.RelationalStorage;

internal static class RelationalSqlClusterOriginalQueryObservation
{
    internal static void Failure(Result<QueryPage> result)
    {
        if (result.IsSuccess)
        {
            return;
        }
        var problem = result.Problem;
        var detail = problem?.Detail;
        var safeDetail = detail switch
        {
            "The SQL syntax or query arguments are invalid." => detail,
            "The bounded relational join is invalid." => detail,
            "The SQL statement or execution clause is unsupported." => detail,
            "The principal cannot perform this operation in this scope." => detail,
            "The query requires a protected field-use grant." => detail,
            _ => null
        };
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            phase = "relational-join-administrator-direct-original",
            errorCode = Enum.TryParse<ErrorCode>(problem?.ErrorCode, out var code) ? code.ToString() : null,
            statusCode = problem?.StatusCode,
            detail = safeDetail,
            detailLength = detail!.Length,
            detailSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(detail)))
        }, JsonDefaults.Options));
    }
}
