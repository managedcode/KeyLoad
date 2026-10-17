using KeyLoad.Server.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Explicit validated native options for standalone text-projection regression owners.</summary>
internal static class UnitNativeTextOptions
{
    internal static IOptions<NativeTextExecutionOptions> Execution(NativeTextExecutionOptions? configured = null)
    {
        var value = configured ?? new NativeTextExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
}
