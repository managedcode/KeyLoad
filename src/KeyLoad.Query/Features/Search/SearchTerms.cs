using System.Text;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class SearchTerms
{
    private const string DocumentTokenExceeded = "The document text token budget is exceeded.";
    private const string WordExceeded = "The text token budget is exceeded.";
    private const int CheckInterval = 512;
    private const int MaxWords = 65_536;
    private const int MaxWordLength = 4_096;

    public static IEnumerable<string> Enumerate(string text, ReadExecutionBudget budget)
    {
        budget.Check();
        var word = new StringBuilder();
        var words = 0;
        var runes = 0;
        foreach (var rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (++runes % CheckInterval == 0)
            {
                budget.Check();
            }
            if (Rune.IsLetterOrDigit(rune))
            {
                word.Append(Rune.ToLowerInvariant(rune));
            }
            else if (word.Length != 0)
            {
                ChargeToken(ref words, budget);
                yield return word.ToString();
                word.Clear();
            }
            if (word.Length > MaxWordLength)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, WordExceeded);
            }
        }
        if (word.Length != 0)
        {
            ChargeToken(ref words, budget);
            yield return word.ToString();
        }
        budget.Check();
    }

    private static void ChargeToken(ref int words, ReadExecutionBudget budget)
    {
        if (++words > MaxWords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DocumentTokenExceeded);
        }
        budget.ChargeTextToken();
    }
}
