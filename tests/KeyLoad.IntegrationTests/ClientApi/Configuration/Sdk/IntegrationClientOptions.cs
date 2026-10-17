using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests;

internal static class IntegrationClientOptions
{
    internal static IOptions<KeyLoadClientExecutionOptions> Execution(KeyLoadClientExecutionOptions? configured = null)
    {
        var value = configured ?? new KeyLoadClientExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<AggregateReplayWorkerLimits> Replay(AggregateReplayWorkerLimits? configured = null)
    {
        var value = configured ?? new AggregateReplayWorkerLimits();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<QueryTranslationOptions> Translation(QueryTranslationOptions? configured = null)
    {
        var value = configured ?? new QueryTranslationOptions();
        value.Validate();
        return Options.Create(value);
    }
}
