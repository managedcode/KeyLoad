using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Owns environment selectors needed before Aspire chooses its dashboard mode.</summary>
[ConfigurationOptions]
internal sealed class TestBootstrapOptions
{
    [ConfigurationKeyName(TestSuiteProtocol.SuiteSetting)] public string? Suite { get; set; }
    [ConfigurationKeyName(TestSuiteProtocol.VectorProfileSetting)] public string? VectorProfile { get; set; }
    [ConfigurationKeyName(TestSuiteProtocol.ScaleProfileSetting)] public string? ScaleProfile { get; set; }
    [ConfigurationKeyName(TestSuiteProtocol.OpenLoopRateSetting)] public string? OpenLoopRate { get; set; }
}
