using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.Query.Search;

internal static class SearchTerms
{
    public static IEnumerable<string> Enumerate(string text, ReadExecutionBudget budget, int checkInterval,
        int maximumWords, int maximumWordLength)
        => CanonicalTextTerms.Enumerate(text, budget, checkInterval, maximumWords, maximumWordLength);
}
