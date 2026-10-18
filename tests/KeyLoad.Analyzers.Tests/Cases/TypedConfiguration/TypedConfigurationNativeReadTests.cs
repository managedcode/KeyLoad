namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-035/037: native provider/debug/environment reads remain inside binding.</summary>
internal sealed class TypedConfigurationNativeReadTests
{
    [Test]
    public async Task NativeProviderReadsAndBareProviderConstructionAreRejectedAsync()
    {
        const string source = """
            using Microsoft.Extensions.Configuration;
            internal sealed class Subject
            {
                private const string Setting = "KeyLoad:Policy";
                private readonly IConfigurationProvider provider;
                internal Subject([|IConfigurationProvider|] provider) => this.provider = provider;
                internal bool Execute(out string value) => [|provider.TryGet(Setting, out value)|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task NativeRootDebugViewAndEnvironmentExpansionAreRawReadsAsync()
    {
        const string source = """
            using Microsoft.Extensions.Configuration;
            internal static class Subject
            {
                private const string EnvironmentPattern = "%KEYLOAD_POLICY%";
                internal static string Read(IConfigurationRoot root) => [|root.GetDebugView()|];
                internal static string Expand() => [|System.Environment.ExpandEnvironmentVariables(EnvironmentPattern)|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task NativeCentralBindingPermitsTheSameActualReadOperationsAsync()
    {
        const string source = """
            using Microsoft.Extensions.Configuration;
            [KeyLoad.ConfigurationBinding]
            internal static class Composition
            {
                private const string Setting = "KeyLoad:Policy";
                private const string EnvironmentPattern = "%KEYLOAD_POLICY%";
                internal static bool Read(IConfigurationProvider provider, out string value) => provider.TryGet(Setting, out value);
                internal static string Inspect(IConfigurationRoot root) => root.GetDebugView();
                internal static string Expand() => System.Environment.ExpandEnvironmentVariables(EnvironmentPattern);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ApplicationProviderAndEnvironmentMethodNamesRemainOrdinaryMethodsAsync()
    {
        const string source = """
            internal sealed class Provider { internal bool TryGet(string key, out string value) { value = key; return true; } }
            internal static class Environment { internal static string ExpandEnvironmentVariables(string value) => value; }
            internal static class Subject
            {
                private const string Identity = "domain.identity";
                internal static bool Execute(Provider provider, out string value) => provider.TryGet(Identity, out value);
                internal static string Expand() => Environment.ExpandEnvironmentVariables(Identity);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
