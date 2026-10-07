using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class Q2CapabilityFixtures
{
    internal static AstQueryRequest Healthy(TestDatabase database)
    {
        var request = SqlInnerJoinRejectionFixture.Ast(database);
        return request with
        {
            Query = request.Query with
            {
                Projection =
            [new("/order_id", "order_id", "l"), new("/name", "customer_name", "r"), new("/total", "total", "l")]
            }
        };
    }

    internal static AstQueryRequest Unsupported(AstQueryRequest request, string kind) => kind switch
    {
        "ast1" => request with { AstVersion = 1 },
        "ast3" => request with { AstVersion = 3 },
        "filter" => request with { Query = request.Query with { Filter = new Comparison(new FieldOperand("/total"), "=", ValueOperand.Create(7)) } },
        "explain" => request with { Query = request.Query with { Explain = true } },
        "alias" => request with { Query = request.Query with { InnerJoin = request.Query.InnerJoin! with { Alias = "l" } } },
        "self" => request with { Query = request.Query with { InnerJoin = request.Query.InnerJoin! with { Collection = "orders" } } },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static AstQueryRequest JsonRoundTrip(AstQueryRequest request)
        => JsonSerializer.Deserialize<AstQueryRequest>(JsonDefaults.Serialize(request), JsonDefaults.Options)!;

    internal static AstQueryRequest NativeRoundTrip(AstQueryRequest request)
        => NativeSerialization.Deserialize<AstQueryRequest>(NativeSerialization.Serialize(request));
}
