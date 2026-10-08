using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalTokens
{
    private const ulong EmptyToken = 0;
    private const ulong InvalidRecord = 0;

    internal static NativeTextIncrementalPosting[] Capture(DocumentRecord? document, string field,
        ulong record, ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        budget.Check();
        if (record == InvalidRecord)
        { throw NativeTextErrors.Corrupt(); }
        if (document is null || document.Deleted)
        { return []; }
        using var json = JsonDocument.Parse(document.Json);
        if (JsonData.Scalar(json.RootElement, JsonData.PathSegments(field)) is not string content)
        { return []; }
        var postings = new List<NativeTextIncrementalPosting>();
        var previous = EmptyToken;
        foreach (var term in CanonicalTextTerms.Enumerate(content, budget, execution.TextBudgetCheckInterval,
                     execution.MaximumDocumentWords, execution.MaximumWordCharacters))
        {
            budget.ChargeBytes(NativeTextIncrementalSourceProtocol.RetainedPostingBytes);
            var token = NativeTextHash.Sha256(term);
            postings.Add(new(token, record, previous));
            previous = token;
        }
        budget.Check();
        return postings.ToArray();
    }
}
