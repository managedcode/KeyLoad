using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;

namespace KeyLoad.Query;

internal static class TextIndexPublication
{
    internal static void Publish(ITextProjectionLease lease, string[] path,
        ReadExecutionBudget budget, DocumentRecord document, QueryExecutionOptions execution)
    {
        budget.Check();
        lease.BeginRecord(document.Reference, document.Revision);
        using var json = JsonDocument.Parse(document.Json);
        if ((JsonData.Scalar(json.RootElement, path)).Value.Value is { Kind: KeyLoad.Storage.KeyComponentKind.Text, Text: var content })
        {
            Span<char> word = stackalloc char[CanonicalTextTerms.InlineWordCapacity];
            using var tokens = new CanonicalTextTerms(content, word, budget, execution.TextBudgetCheckInterval,
                execution.MaximumDocumentWords, execution.MaximumWordCharacters);
            while (tokens.MoveNext())
            {
                lease.ObserveToken(tokens.Current);
            }
        }
    }
}
