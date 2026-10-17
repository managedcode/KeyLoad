using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Bounds client expression translation before any query reaches the server.</summary>
[ConfigurationOptions]
public sealed class QueryTranslationOptions
{
    /// <summary>The client translator configuration section.</summary>
    public const string SectionName = "KeyLoad:QueryTranslation";
    /// <summary>The rejection for invalid translation budgets.</summary>
    public const string ValidationMessage = "Query translation budgets must be positive and within their existing ceilings.";
    private const int DefaultPageLimit = 100;
    private const int MaximumTranslationDepth = 32;
    private const int MaximumCollectionItems = 256;
    private const int MinimumPositiveLimit = 1;

    /// <summary>The initial query page size; callers may explicitly request another valid page size.</summary>
    public int DefaultQueryLimit { get; set; } = DefaultPageLimit;
    /// <summary>The maximum predicate, field path and constant expression depth.</summary>
    public int MaximumDepth { get; set; } = MaximumTranslationDepth;
    /// <summary>The maximum number of translated IN values.</summary>
    public int MaximumInItems { get; set; } = MaximumCollectionItems;
    /// <summary>The maximum array items evaluated from a constant expression.</summary>
    public int MaximumConstantArrayItems { get; set; } = MaximumCollectionItems;

    /// <summary>Checks positive budgets against the translator's finite domain.</summary>
    /// <returns>Whether the translator can consume these settings.</returns>
    public bool IsValid() => DefaultQueryLimit >= MinimumPositiveLimit
        && MaximumDepth is >= MinimumPositiveLimit and <= MaximumTranslationDepth
        && MaximumInItems is >= MinimumPositiveLimit and <= MaximumCollectionItems
        && MaximumConstantArrayItems is >= MinimumPositiveLimit and <= MaximumCollectionItems;

    /// <summary>Rejects invalid standalone composition before evaluating expressions.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(QueryTranslationOptions), [ValidationMessage]);
        }
    }
}
