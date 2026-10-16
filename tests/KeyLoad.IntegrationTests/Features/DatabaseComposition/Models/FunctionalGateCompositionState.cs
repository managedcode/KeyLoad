using System.Globalization;
using System.Text.Json;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateCompositionProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateCompositionState(string Id, string Collection, int Iteration)
{
    private const string IdentifierParameter = "id";
    private const string TitleParameter = "title";
    private const string AmountParameter = "amount";
    private const string DocumentIdParameter = "doc_id";
    private const string AnchorReferenceParameter = "anchor_ref";

    internal static FunctionalGateCompositionState Create(FunctionalGateContext context, int iteration)
        => new("composition-" + iteration.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"),
            context.MutableDocumentCollection, iteration);

    internal string Row(FunctionalGateContext context, bool linked) => JsonSerializer.Serialize(new
    {
        amount = Iteration,
        anchor_ref = JsonSerializer.Serialize(context.Bind(context.Anchor), JsonDefaults.Options),
        doc_id = linked ? Id : context.Bind(context.Anchor).Id,
        id = Id,
        title = P.Kind
    }, JsonDefaults.Options);

    internal string Document(FunctionalGateContext context, bool linked)
        => linked ? JsonSerializer.Serialize(new
        {
            docId = Id,
            kind = P.Kind,
            anchor = context.Bind(context.Anchor),
            typed = context.Bind(new EntityRef(context.Partition, P.Table, Id))
        }, JsonDefaults.Options) : JsonSerializer.Serialize(new
        {
            anchor = CanonicalAnchor(context),
            docId = Id,
            kind = P.Kind
        }, JsonDefaults.Options);

    // Independent test serialization preserves every bound-reference field in ordinal PATCH order.
    private static JsonElement CanonicalAnchor(FunctionalGateContext context)
        => JsonSerializer.Deserialize<JsonElement>(FunctionalGateBoundJson.Serialize(context.Bind(context.Anchor)));

    internal SqlOperationRequest Insert(FunctionalGateContext context)
        => new(context.Partition, P.InsertSql, new(StringComparer.Ordinal)
        {
            [IdentifierParameter] = JsonSerializer.SerializeToElement(Id),
            [TitleParameter] = JsonSerializer.SerializeToElement(P.Kind),
            [AmountParameter] = JsonSerializer.SerializeToElement(Iteration),
            [DocumentIdParameter] = JsonSerializer.SerializeToElement(context.Bind(context.Anchor).Id),
            [AnchorReferenceParameter] = JsonSerializer.SerializeToElement(JsonSerializer.Serialize(context.Bind(context.Anchor), JsonDefaults.Options))
        }, CommandId: Guid.NewGuid());
}
