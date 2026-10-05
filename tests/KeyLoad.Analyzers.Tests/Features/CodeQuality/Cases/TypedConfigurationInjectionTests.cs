namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-034/037: native IOptions is the execution owner's configuration boundary.</summary>
internal sealed class TypedConfigurationInjectionTests
{
    [Test]
    public async Task BareConstructorAndUnprovenSnapshotInjectionAreRejectedAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int RetryLimit { get; set; } }
            internal sealed class Subject
            {
                private readonly [|Policy|] snapshot;
                internal Subject([|Policy|] policy) { snapshot = policy; }
                internal int Execute() => snapshot.RetryLimit;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task PublicPropertyInjectionAndDirectConfigurationConstructionAreRejectedAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int RetryLimit { get; set; } }
            internal sealed class Subject
            {
                public [|Policy|] Configuration { get; set; }
                internal Policy Execute() => [|new Policy()|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task NativeOptionsValueCaptureAndPrivateSnapshotHelpersPreserveOwnershipAsync()
    {
        const string source = """
            using Microsoft.Extensions.Options;
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public System.TimeSpan Deadline { get; set; } }
            internal sealed class Subject
            {
                private readonly Policy snapshot;
                internal Subject(IOptions<Policy> options) { snapshot = options.Value; }
                internal System.Threading.Tasks.Task Execute() => Run(snapshot);
                private static System.Threading.Tasks.Task Run(Policy policy) =>
                    System.Threading.Tasks.Task.Delay(policy.Deadline);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ActualBindingTypeOwnsValidatedStandaloneConstructionAsync()
    {
        const string source = """
            using Microsoft.Extensions.Options;
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int RetryLimit { get; set; } }
            [KeyLoad.ConfigurationBinding]
            internal static class Composition
            {
                internal static IOptions<Policy> Bind() => Options.Create(new Policy());
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task GeneratedAndExternalConfigurationCallsRetainTheirBoundariesAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const string Setting = "KEYLOAD_POLICY";
                internal static string Execute() => System.Environment.GetEnvironmentVariable(Setting);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source, path: AnalyzerFixture.GeneratedPath);
        await MagicRuntimeFixture.AssertConfigurationAsync(source, assemblyName: AnalyzerFixture.ExternalAssembly);
        await MagicRuntimeFixture.AssertConfigurationAsync(source, assemblyName: AnalyzerFixture.UnitTestAssembly);
    }
}
