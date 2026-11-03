using System.Buffers;
using System.Text;

namespace KeyLoad.Core.Search;

internal ref struct CanonicalTextTerms
{
    internal const int InlineWordCapacity = 64;
    private const int BufferGrowthFactor = 2;
    private const string DocumentTokenExceeded = "The document text token budget is exceeded.";
    private const string WordExceeded = "The text token budget is exceeded.";
    private readonly ReadExecutionBudget budget;
    private readonly int checkInterval;
    private readonly int maximumWords;
    private readonly int maximumWordLength;
    private StringRuneEnumerator source;
    private Span<char> word;
    private char[]? rented;
    private int length;
    private int words;
    private int runes;

    internal CanonicalTextTerms(string text, Span<char> word, ReadExecutionBudget budget,
        int checkInterval, int maximumWords, int maximumWordLength)
    {
        budget.Check();
        source = text.Normalize(NormalizationForm.FormKC).EnumerateRunes();
        this.word = word;
        this.budget = budget;
        this.checkInterval = checkInterval;
        this.maximumWords = maximumWords;
        this.maximumWordLength = maximumWordLength;
    }

    public readonly ReadOnlySpan<char> Current => word[..length];

    public bool MoveNext()
    {
        length = 0;
        while (source.MoveNext())
        {
            if (++runes % checkInterval == 0)
            {
                budget.Check();
            }
            var rune = source.Current;
            if (Rune.IsLetterOrDigit(rune))
            {
                AppendRune(Rune.ToLowerInvariant(rune));
            }
            else if (length != 0)
            {
                return ChargeToken();
            }
        }
        if (length != 0)
        {
            return ChargeToken();
        }
        budget.Check();
        return false;
    }

    public void Dispose()
    {
        if (rented is not null)
        {
            ArrayPool<char>.Shared.Return(rented, clearArray: true);
            rented = null;
        }
    }

    private void AppendRune(Rune rune)
    {
        var units = rune.Utf16SequenceLength;
        if (units > maximumWordLength - length)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, WordExceeded);
        }
        var required = length + units;
        if (required > word.Length)
        {
            var capacity = (int)Math.Min(maximumWordLength, (long)word.Length * BufferGrowthFactor);
            var enlarged = ArrayPool<char>.Shared.Rent(Math.Max(required, capacity));
            word[..length].CopyTo(enlarged);
            Dispose();
            rented = enlarged;
            word = enlarged;
        }
        rune.EncodeToUtf16(word[length..]);
        length = required;
    }

    private bool ChargeToken()
    {
        if (++words > maximumWords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DocumentTokenExceeded);
        }
        return true;
    }
}
