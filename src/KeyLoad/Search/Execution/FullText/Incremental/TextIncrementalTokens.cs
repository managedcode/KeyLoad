using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalTokens
{
    private const ulong EmptyToken = 0;
    private const ulong InvalidRecord = 0;

    internal static TextIncrementalPosting[] Capture(DocumentRecord? document, string field,
        ulong record, ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        budget.Check();
        if (record == InvalidRecord)
        {
            throw TextErrors.Corrupt();
        }
        if (document is null || document.Deleted)
        {
            return [];
        }
        using var json = JsonDocument.Parse(document.Json);
        if ((JsonData.Scalar(json.RootElement, JsonData.PathSegments(field))).Value.Value is not { Kind: KeyLoad.Storage.KeyComponentKind.Text, Text: var content })
        {
            return [];
        }
        var postings = new List<TextIncrementalPosting>();
        var previous = EmptyToken;
        Span<char> word = stackalloc char[CanonicalTextTerms.InlineWordCapacity];
        using var tokens = new CanonicalTextTerms(content, word, budget, execution.TextBudgetCheckInterval,
            execution.MaximumDocumentWords, execution.MaximumWordCharacters);
        while (tokens.MoveNext())
        {
            var token = TextHash.Sha256(tokens.Current.ToString());
            postings.Add(new(token, record, previous));
            previous = token;
        }
        budget.Check();
        return postings.ToArray();
    }
}
