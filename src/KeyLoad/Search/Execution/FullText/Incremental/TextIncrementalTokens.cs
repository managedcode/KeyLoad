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
        if ((JsonData.Scalar(json.RootElement, JsonData.PathSegments(field))).Value.Value is not string content)
        {
            return [];
        }
        var postings = new List<TextIncrementalPosting>();
        var previous = EmptyToken;
        foreach (var term in CanonicalTextTerms.Enumerate(content, budget, execution.TextBudgetCheckInterval,
                     execution.MaximumDocumentWords, execution.MaximumWordCharacters))
        {
            budget.ChargeBytes(TextIncrementalSourceProtocol.RetainedPostingBytes);
            var token = TextHash.Sha256(term);
            postings.Add(new(token, record, previous));
            previous = token;
        }
        budget.Check();
        return postings.ToArray();
    }
}
