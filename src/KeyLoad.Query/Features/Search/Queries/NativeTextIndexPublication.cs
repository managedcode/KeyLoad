using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query;

public sealed partial class SearchEngine
{
    private void PublishTextIndexRecord(ITextProjectionLease lease, string[] path,
        ReadExecutionBudget budget, DocumentRecord document)
    {
        budget.Check();
        lease.BeginRecord(document.Reference, document.Revision);
        using var json = JsonDocument.Parse(document.Json);
        if (JsonData.Scalar(json.RootElement, path) is string content)
        {
            foreach (var term in SearchTerms.Enumerate(content, budget, execution.TextBudgetCheckInterval,
                execution.MaximumDocumentWords, execution.MaximumWordCharacters))
            {
                lease.ObserveToken(term);
            }
        }
    }
}
