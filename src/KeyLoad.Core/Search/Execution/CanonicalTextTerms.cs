using System.Text;

namespace KeyLoad.Core.Search;

internal static class CanonicalTextTerms
{
    private const string DocumentTokenExceeded = "The document text token budget is exceeded.";
    private const string WordExceeded = "The text token budget is exceeded.";

    public static IEnumerable<string> Enumerate(string text, ReadExecutionBudget budget, int checkInterval,
        int maximumWords, int maximumWordLength)
    {
        budget.Check();
        var word = new StringBuilder();
        var words = 0;
        var runes = 0;
        foreach (var rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (++runes % checkInterval == 0)
            {
                budget.Check();
            }
            if (Rune.IsLetterOrDigit(rune))
            {
                word.Append(Rune.ToLowerInvariant(rune));
            }
            else if (word.Length != 0)
            {
                ChargeToken(ref words, budget, maximumWords);
                yield return word.ToString();
                word.Clear();
            }
            if (word.Length > maximumWordLength)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, WordExceeded);
            }
        }
        if (word.Length != 0)
        {
            ChargeToken(ref words, budget, maximumWords);
            yield return word.ToString();
        }
        budget.Check();
    }

    private static void ChargeToken(ref int words, ReadExecutionBudget budget, int maximumWords)
    {
        if (++words > maximumWords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DocumentTokenExceeded);
        }
        budget.ChargeTextToken();
    }
}
