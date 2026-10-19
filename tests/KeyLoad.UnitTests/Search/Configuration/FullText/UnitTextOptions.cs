using KeyLoad.Server.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Explicit validated execution options for standalone text-projection owners.</summary>
internal static class UnitTextOptions
{
    internal static IOptions<TextExecutionOptions> Execution(TextExecutionOptions? configured = null)
    {
        var value = configured ?? new TextExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
}
