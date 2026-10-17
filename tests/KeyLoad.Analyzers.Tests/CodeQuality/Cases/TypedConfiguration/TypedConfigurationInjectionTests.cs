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
            internal sealed class Policy
            {
                public int RetryLimit { get; set; } = 3;
                internal void Validate() => System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RetryLimit);
            }
            [KeyLoad.ConfigurationBinding]
            internal static class Composition
            {
                internal static IOptions<Policy> Bind()
                {
                    var policy = new Policy();
                    policy.Validate();
                    return Options.Create(policy);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task NativeOptionsCreateCannotHideAnUnownedPolicySnapshotAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int RetryLimit { get; set; } }
            internal static class Subject
            {
                internal static Microsoft.Extensions.Options.IOptions<Policy> Execute(Policy supplied) =>
                    [|Microsoft.Extensions.Options.Options.Create(supplied)|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task NativeFactoryAndManagerRemainOwnedByActualBindingAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int RetryLimit { get; set; } }
            internal static class Subject
            {
                internal static Microsoft.Extensions.Options.IOptions<Policy> Execute()
                {
                    var factory = [|new Microsoft.Extensions.Options.OptionsFactory<Policy>([], [])|];
                    return [|new Microsoft.Extensions.Options.OptionsManager<Policy>(factory)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task GetOnlyPropertyCapturesTheActualOptionsValueOnceAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public System.TimeSpan Deadline { get; set; } }
            internal sealed class Subject
            {
                public Policy Snapshot { get; }
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> options) { Snapshot = options.Value; }
                internal System.Threading.Tasks.Task Execute() => System.Threading.Tasks.Task.Delay(Snapshot.Deadline);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task UnprovenGetOnlyPropertyRemainsBareInjectionAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int RetryLimit { get; set; } }
            internal sealed class Subject
            {
                public [|Policy|] Snapshot { get; }
                internal Subject([|Policy|] supplied) { Snapshot = supplied; }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ReassigningAGetOnlyPropertyDoesNotProveASingleFrozenSnapshotAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int RetryLimit { get; set; } }
            internal sealed class Subject
            {
                public [|Policy|] Snapshot { get; }
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> options)
                {
                    Snapshot = options.Value;
                    Snapshot = options.Value;
                }
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
