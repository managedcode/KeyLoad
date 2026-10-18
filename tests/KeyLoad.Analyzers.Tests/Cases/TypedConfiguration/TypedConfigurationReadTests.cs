namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-035/037: native providers remain inside actual metadata-owned binding boundaries.</summary>
internal sealed class TypedConfigurationReadTests
{
    [Test]
    [Arguments("return [|configuration[Setting]|];")]
    [Arguments("return [|configuration.GetValue<string>(Setting)|];")]
    [Arguments("return [|configuration.GetSection(Setting)|];")]
    [Arguments("return [|System.Environment.GetEnvironmentVariable(Setting)|];")]
    [Arguments("return [|System.Environment.GetEnvironmentVariables()|];")]
    public async Task RuntimeRawProviderAndEnvironmentReadsAreRejectedAsync(string operation)
    {
        var source = $$"""
            using Microsoft.Extensions.Configuration;
            internal static class Subject
            {
                private const string Setting = "KeyLoad:Policy";
                internal static object Execute(IConfiguration configuration) { {{operation}} }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ActualBindingMethodPermitsNativeProviderOperationsAsync()
    {
        const string source = """
            using Microsoft.Extensions.Configuration;
            internal static class Subject
            {
                private const string Setting = "KeyLoad:Policy";
                [KeyLoad.ConfigurationBinding]
                internal static string Bind(IConfiguration configuration) =>
                    configuration[Setting] ?? System.Environment.GetEnvironmentVariable(Setting);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ABindingClassNameDoesNotGrantMetadataOwnershipAsync()
    {
        const string source = """
            internal static class ConfigurationBinding
            {
                private const string Setting = "KEYLOAD_POLICY";
                internal static string Execute() => [|System.Environment.GetEnvironmentVariable(Setting)|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ConfiguredOptionsParametersDoNotReadRawProvidersAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int RetryLimit { get; set; } }
            internal static class Subject
            {
                internal static int Execute(Microsoft.Extensions.Options.IOptions<Policy> options) => options.Value.RetryLimit;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
