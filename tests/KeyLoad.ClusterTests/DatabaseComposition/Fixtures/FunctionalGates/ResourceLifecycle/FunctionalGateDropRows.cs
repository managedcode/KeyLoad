using System.Text.Json;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateDropRows
{
    internal const string OriginalId = "drop-original";
    internal const string FreshId = "drop-fresh";
    private const string Title = "drop-lifetime";
    private const int Amount = 17;
    private const int First = 0;
    private const int Second = 1;

    internal static string Json(FunctionalGateContext context, int index, string id)
        => index switch
        {
            First => JsonSerializer.Serialize(new
            {
                id,
                title = Title,
                amount = Amount,
                doc_id = context.Bind(context.Anchor).Id,
                anchor_ref = JsonSerializer.Serialize(context.Bind(context.Anchor), JsonDefaults.Options)
            }, JsonDefaults.Options),
            Second => FunctionalGateRelationalRows.Child(id, id),
            _ => FunctionalGateRelationalRows.Independent(id)
        };

    internal static EntityRef Reference(FunctionalGateContext context, int index, string id)
        => new(context.Partition, P.Tables[index], id);
}
