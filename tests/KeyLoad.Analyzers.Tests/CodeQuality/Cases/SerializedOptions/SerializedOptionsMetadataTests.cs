namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-037: serialized record metadata preserves its shape without granting execution ownership.</summary>
internal sealed class SerializedOptionsMetadataTests
{
    private const string PolicyDeclaration = """
        [KeyLoad.ConfigurationOptions]
        internal sealed record Policy { public int Limit { get; init; } = 4; }

        """;

    [Test]
    public async Task GenuinePositionalReportManifestAndStreamMetadataRemainDataAsync()
    {
        const string source = """
            internal sealed record Report([property: KeyLoad.SerializedOptionsSnapshot] Policy? Options);
            internal sealed record Manifest
            {
                [KeyLoad.SerializedOptionsSnapshot]
                [System.Text.Json.Serialization.JsonRequired]
                public Policy Options { get; init; } = null!;
            }
            internal sealed record Streamed([property: KeyLoad.SerializedOptionsSnapshot] Policy? Options)
            {
                internal static Streamed Project(Report source) => new(source.Options);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(PolicyDeclaration + source);
    }

    [Test]
    public async Task RecordMetadataDoesNotGrantConstructorReadOrDeadlineOwnershipAsync()
    {
        const string source = """
            internal sealed record Report([property: KeyLoad.SerializedOptionsSnapshot] Policy? Options)
            {
                private const int DeadlineMilliseconds = 4;
                private const string Key = "runtime";
                internal Report([|Policy|] other, int tag) : this(other) { }
                internal static string? Read(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    [|configuration[Key]|];
                internal static System.Threading.Tasks.Task Execute() =>
                    [|System.Threading.Tasks.Task.Delay(DeadlineMilliseconds)|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(PolicyDeclaration + source);
    }

    [Test]
    public async Task MutableRecordAndOrdinaryServiceStillRejectBareConfigurationAsync()
    {
        const string source = """
            internal sealed record Mutable
            {
                [KeyLoad.SerializedOptionsSnapshot]
                public [|Policy|] Options { get; set; } = null!;
            }
            internal sealed class Service([|Policy|] options)
            {
                [KeyLoad.SerializedOptionsSnapshot]
                public [|Policy|] Options { get; init; } = options;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(PolicyDeclaration + source);
    }

    [Test]
    public async Task CounterfeitSnapshotMarkerDoesNotExemptTheRecordParameterAsync()
    {
        const string source = """
            namespace Counterfeit
            {
                internal sealed class SerializedOptionsSnapshotAttribute : System.Attribute { }
                internal sealed record Report([property: SerializedOptionsSnapshot] [|Policy|] Options);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(PolicyDeclaration + source);
    }

    [Test]
    public async Task AuthoredRecordAccessorsCannotClaimAutomaticSnapshotOwnershipAsync()
    {
        const string source = """
            internal sealed record Dynamic
            {
                [KeyLoad.SerializedOptionsSnapshot]
                public [|Policy|] Options { get => null!; init { } }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(PolicyDeclaration + source);
    }

    [Test]
    public async Task UnrelatedMutableInstanceStatePreventsSnapshotOwnershipAsync()
    {
        const string source = """
            internal sealed record MutableField([property: KeyLoad.SerializedOptionsSnapshot] [|Policy|] Options)
            {
                internal int Sequence;
            }
            internal sealed record MutableProperty
            {
                [KeyLoad.SerializedOptionsSnapshot]
                public [|Policy|] Options { get; init; } = null!;
                internal int Sequence { get; set; }
            }
            internal record MutableBase { internal int Sequence { get; set; } }
            internal sealed record Derived([property: KeyLoad.SerializedOptionsSnapshot] [|Policy|] Options) : MutableBase;
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(PolicyDeclaration + source);
    }

    [Test]
    public async Task StaticCachesReadonlyFieldsAndPureMethodsPreserveSnapshotOwnershipAsync()
    {
        const string source = """
            internal sealed record Report([property: KeyLoad.SerializedOptionsSnapshot] Policy? Options)
            {
                private readonly int sequence;
                internal int Sequence { get; init; }
                internal static Report? Cache { get; set; }
                internal static int CachedSequence;
                internal bool IsValid() => Options is not null && Sequence >= sequence;
                internal Report Project() => this with { Sequence = Sequence };
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(PolicyDeclaration + source);
    }

    [Test]
    public async Task NativeFactoryValueCanBeExposedByAReadonlyGetterAsync()
    {
        const string source = """
            internal sealed class Selection
            {
                public Policy Options => CreateExecutionOptions().Value;
                [KeyLoad.ConfigurationBinding]
                internal Microsoft.Extensions.Options.IOptions<Policy> CreateExecutionOptions() =>
                    Microsoft.Extensions.Options.Options.Create(new Policy());
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(PolicyDeclaration + source);
    }

    [Test]
    public async Task APropertyNamedValueCannotForgeTheNativeOptionsExportAsync()
    {
        const string source = """
            internal sealed class FakeOptions { internal Policy Value => null!; }
            internal sealed class Owner(FakeOptions source)
            {
                public [|Policy|] Options => source.Value;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(PolicyDeclaration + source);
    }
}
