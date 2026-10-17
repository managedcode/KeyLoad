namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-037: an empty scheduling signal retains its configured admission authority.</summary>
internal sealed class ConfiguredCountingSignalTests
{
    [Test]
    public async Task AConfiguredMaximumAllowsAnInitiallyEmptyNativeSignalAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Capacity { get; set; } = 64; }
            internal static class Subject
            {
                private const int NoReadyEntries = 0;
                internal static System.Threading.SemaphoreSlim Execute(Microsoft.Extensions.Options.IOptions<Policy> options) =>
                    new(NoReadyEntries, options.Value.Capacity);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task APrivateSaturatedHelperReadsTheNativeValueBackedReadonlyExportAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy
            {
                public int Capacity { get; set; } = 64;
                public int ReservedControlCommands { get; set; } = 8;
            }
            internal sealed class Admission
            {
                private readonly Policy? snapshot;
                internal Admission(Microsoft.Extensions.Options.IOptions<Policy> options) => snapshot = options.Value;
                public Policy Limits => snapshot ?? throw new System.InvalidOperationException();
            }
            internal static class Subject
            {
                private const int NoReadyEntries = 0;
                private const int StopWakeCount = 1;
                internal static System.Threading.SemaphoreSlim Execute(Admission admission) => new(NoReadyEntries, Maximum(admission));
                private static int Maximum(Admission admission)
                {
                    System.ArgumentNullException.ThrowIfNull(admission);
                    var configured = admission.Limits;
                    return (int)System.Math.Min(int.MaxValue, (long)configured.Capacity + configured.ReservedControlCommands + StopWakeCount);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    [Arguments("Maximum", "private static int Maximum() => FixedCapacity;")]
    [Arguments("FixedCapacity", "")]
    public async Task FixedCapacitiesCannotBorrowTheEmptySignalIdentityAsync(string capacity, string helper)
    {
        var argument = helper.Length > 0 ? capacity + "()" : capacity;
        var source = $$"""
            internal static class Subject
            {
                private const int NoReadyEntries = 0;
                private const int FixedCapacity = 64;
                internal static System.Threading.SemaphoreSlim Execute() =>
                    [|new(NoReadyEntries, {{argument}})|];
                {{helper}}
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ConfiguredAdmissionDoesNotAuthorizeAnArbitraryAddedCapacityAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Capacity { get; set; } = 64; }
            internal static class Subject
            {
                private const int NoReadyEntries = 0;
                private const int HiddenExtraCapacity = 64;
                internal static System.Threading.SemaphoreSlim Execute(Microsoft.Extensions.Options.IOptions<Policy> options) =>
                    [|new(NoReadyEntries, options.Value.Capacity + HiddenExtraCapacity)|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
