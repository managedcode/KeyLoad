namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-034/037: a native Value base cannot authorize hidden clone defaults.</summary>
internal sealed class OptionsSnapshotOverrideTests
{
    [Test]
    public async Task AConstOverrideInvalidatesTheSnapshotCaptureAndTheCloneOperationAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed record Policy { public int BufferBytes { get; init; } = 65536; }
            internal sealed class Subject
            {
                private const int HiddenBufferBytes = 32768;
                private readonly [|Policy|] snapshot;
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> configured) =>
                    snapshot = [|configured.Value with { BufferBytes = HiddenBufferBytes }|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task HiddenGetterStringAndBooleanOverridesCannotBorrowTheBaseValueAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed record Policy
            {
                public string Endpoint { get; init; } = "localhost";
                public bool Enabled { get; init; } = true;
            }
            internal static class Subject
            {
                private const string HiddenEndpoint = "remote";
                private const bool HiddenEnabled = false;
                private static string Endpoint => HiddenEndpoint;
                internal static Policy Execute(Microsoft.Extensions.Options.IOptions<Policy> configured) =>
                    [|configured.Value with { Endpoint = Endpoint, Enabled = HiddenEnabled }|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task EmptyConfiguredAndCallerSuppliedCloneValuesKeepTheirActualSourcesAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed record Policy { public int BufferBytes { get; init; } = 65536; }
            internal sealed class Subject
            {
                private readonly Policy snapshot;
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> configured)
                {
                    snapshot = configured.Value with { BufferBytes = configured.Value.BufferBytes };
                }
                internal Policy Execute(int supplied) => snapshot with { BufferBytes = supplied };
                internal Policy Copy() => snapshot with { };
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ActualBindingAndUnrelatedDomainRecordOverridesRemainValidAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed record Policy { public int BufferBytes { get; init; } = 65536; }
            internal sealed record Payload(int Identity);
            internal static class Subject
            {
                private const int ConfiguredBuffer = 32768;
                private const int DomainIdentity = 7;
                [KeyLoad.ConfigurationBinding]
                internal static Policy Bind(Microsoft.Extensions.Options.IOptions<Policy> configured) =>
                    configured.Value with { BufferBytes = ConfiguredBuffer };
                internal static Payload Represent(Payload payload) => payload with { Identity = DomainIdentity };
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
