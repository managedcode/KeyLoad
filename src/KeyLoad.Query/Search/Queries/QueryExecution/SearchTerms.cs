using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Query.Features.Search;

internal static class SearchTerms
{
    public static IEnumerable<string> Enumerate(string text, ReadExecutionBudget budget, int checkInterval,
        int maximumWords, int maximumWordLength)
        => CanonicalTextTerms.Enumerate(text, budget, checkInterval, maximumWords, maximumWordLength);
}
