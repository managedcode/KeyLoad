using Microsoft.Extensions.Options;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

[ConfigurationBinding]
internal static class SharedDomainProcessConfiguration
{
    internal static IOptions<SharedDomainProcessExecutionOptions> Create()
    {
        var settings = new SharedDomainProcessExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }
}
