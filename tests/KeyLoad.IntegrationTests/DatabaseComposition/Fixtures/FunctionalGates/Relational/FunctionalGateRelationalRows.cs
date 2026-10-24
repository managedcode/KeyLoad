using System.Globalization;
using System.Text.Json;
using P = KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateRelationalRows
{
    private const string IterationPrefix = "iteration-";
    private const string Separator = "-";
    private const string GuidFormat = "N";
    private const string ParentTitle = "parent";
    private const string ChildLabel = "child";
    private const long InitialAmount = 10;

    internal static string Identity(int iteration)
        => IterationPrefix + iteration.ToString(CultureInfo.InvariantCulture) + Separator + Guid.NewGuid().ToString(GuidFormat);

    internal static string Parent(FunctionalGateContext ctx)
        => JsonSerializer.Serialize(new
        {
            id = P.Baseline,
            title = ParentTitle,
            amount = InitialAmount,
            doc_id = ctx.Bind(ctx.Anchor).Id,
            anchor_ref = JsonSerializer.Serialize(ctx.Bind(ctx.Anchor), JsonDefaults.Options)
        }, JsonDefaults.Options);

    internal static string Child(string id, string? parent)
        => JsonSerializer.Serialize(new
        {
            id,
            parent_id = parent,
            label = ChildLabel
        }, JsonDefaults.Options);

    internal static string Independent(string id)
        => JsonSerializer.Serialize(new
        {
            id,
            active = true
        }, JsonDefaults.Options);

    internal static Dictionary<string, JsonElement> Parameters(string id)
        => new(StringComparer.Ordinal)
        {
            [P.Id] = JsonSerializer.SerializeToElement(id, JsonDefaults.Options)
        };

    internal static Dictionary<string, JsonElement> InsertParameters(FunctionalGateContext ctx, string id)
    {
        var parameters = Parameters(id);
        parameters.Add(P.DocumentId, JsonSerializer.SerializeToElement(ctx.Bind(ctx.Anchor).Id, JsonDefaults.Options));
        parameters.Add(P.AnchorReference, JsonSerializer.SerializeToElement(
            JsonSerializer.Serialize(ctx.Bind(ctx.Anchor), JsonDefaults.Options), JsonDefaults.Options));
        return parameters;
    }

    internal static string JoinJson(string id)
        => "{\"child_id\":" + JsonSerializer.Serialize(id, JsonDefaults.Options)
            + ",\"parent_title\":\"transient\",\"amount\":11}";
}
