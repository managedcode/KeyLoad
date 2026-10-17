using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

internal sealed class QueryTranslationContext(IOptions<QueryTranslationOptions> options)
{
    private readonly QueryTranslationOptions limits = options.Value;
    internal QueryTranslationOptions Limits => limits;
    internal QueryExpressions Expressions { get; } = new(options);
}
