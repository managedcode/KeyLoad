using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class FoundationOtlpRf3Options
{
    internal static IOptions<FoundationOtlpRf3ExecutionOptions> Create(FoundationOtlpRf3ExecutionOptions? configured = null)
    {
        var value = configured ?? new FoundationOtlpRf3ExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
}
