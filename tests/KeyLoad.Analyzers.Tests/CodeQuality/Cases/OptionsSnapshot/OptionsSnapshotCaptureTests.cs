namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-037: actual Value flow proves a single frozen snapshot, not a subtree occurrence.</summary>
internal sealed class OptionsSnapshotCaptureTests
{
    [Test]
    [Arguments("snapshot")]
    [Arguments("snapshot ?? throw new System.InvalidOperationException()")]
    public async Task AReadonlyExportRetainsTheSingleActualValueCaptureAsync(string getter)
    {
        var source = $$"""
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Capacity { get; set; } }
            internal sealed class Subject
            {
                private readonly Policy snapshot;
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> options) => snapshot = options.Value;
                public Policy Limits => {{getter}};
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task AnUnassignedLocalAliasAndReadonlyRecordCloneRetainTheNativeValueSourceAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed record Policy { public int Capacity { get; init; } = 64; }
            internal sealed class Subject
            {
                private readonly Policy snapshot;
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> options)
                {
                    var validated = options.Value;
                    snapshot = validated with { };
                }
                internal int Execute() => snapshot.Capacity;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task AReassignedAliasCannotProveTheOptionsSourceAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Capacity { get; set; } }
            internal sealed class Subject
            {
                private readonly [|Policy|] snapshot;
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> options)
                {
                    var selected = options.Value;
                    selected = [|new Policy()|];
                    snapshot = selected;
                }
                internal int Execute() => snapshot.Capacity;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task AnUnvalidatedConditionalBranchCannotBorrowAnotherBranchesValueReadAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Capacity { get; set; } }
            internal sealed class Subject
            {
                private readonly [|Policy|] snapshot;
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> options, bool useConfigured)
                {
                    snapshot = useConfigured ? options.Value : [|new Policy()|];
                }
                internal int Execute() => snapshot.Capacity;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ReassigningAReadonlyFieldDoesNotProveASingleSnapshotAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Capacity { get; set; } }
            internal sealed class Subject
            {
                private readonly [|Policy|] snapshot;
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> options)
                {
                    snapshot = options.Value;
                    snapshot = options.Value;
                }
                internal int Execute() => snapshot.Capacity;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
