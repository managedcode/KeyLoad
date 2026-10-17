using System.Text;

namespace KeyLoad.Core.Features.Search;

internal static class CanonicalTextTerms
{
    private const int EmptyElementCount = 0;
    private const int BudgetCheckRemainder = 0;

    private const string DocumentTokenExceeded = "The document text token budget is exceeded.";
    private const string WordExceeded = "The text token budget is exceeded.";

    public static IEnumerable<string> Enumerate(string text, ReadExecutionBudget budget, int checkInterval,
        int maximumWords, int maximumWordLength)
    {
        budget.Check();
        var word = new StringBuilder();
        var words = EmptyElementCount;
        var runes = EmptyElementCount;
        foreach (var rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (++runes % checkInterval == BudgetCheckRemainder)
            {
                budget.Check();
            }
            if (Rune.IsLetterOrDigit(rune))
            {
                word.Append(Rune.ToLowerInvariant(rune));
            }
            else if (word.Length != EmptyElementCount)
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
        if (word.Length != EmptyElementCount)
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
