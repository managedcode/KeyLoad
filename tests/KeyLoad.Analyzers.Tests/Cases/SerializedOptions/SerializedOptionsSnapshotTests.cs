namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-034/037: actual immutable serializer output data is distinct from runtime injection.</summary>
internal sealed class SerializedOptionsSnapshotTests
{
    [Test]
    [Arguments("NodeAdmissionStatus")]
    [Arguments("HttpAdmissionStatus")]
    public async Task ActualPositionalSerializerDataCarriesAFrozenOptionsSnapshotAsync(string contractName)
    {
        var source = $$"""
            internal static class Identities { internal const string Status = "keyload.status.snapshot"; }
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int AdmissionCapacity { get; set; } }
            [Orleans.GenerateSerializer, Orleans.Alias(Identities.Status)]
            internal sealed record {{contractName}}([property: Orleans.Id(0)] Policy Limits,
                [property: Orleans.Id(1)] int Usage);
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
        await MagicRuntimeFixture.AssertDurationAsync(source);
        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task AGeneralSerializationAnnotationCannotGrantTheNativeContractBoundaryAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int AdmissionCapacity { get; set; } }
            [System.Serializable]
            internal sealed record SerializerContract([property: Orleans.Id(0)] [|Policy|] Limits);
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task AnExecutableOwnerCannotHideInjectionBehindActualSerializerMetadataAsync()
    {
        const string source = """
            internal static class Identities { internal const string Status = "keyload.status.snapshot"; }
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int AdmissionCapacity { get; set; } }
            [Orleans.GenerateSerializer, Orleans.Alias(Identities.Status)]
            internal sealed record Subject([property: Orleans.Id(0)] [|Policy|] Limits)
            {
                internal int Execute() => Limits.AdmissionCapacity;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task PropertyBodiesDoNotQualifyAsSerializedDataOnlyAsync()
    {
        const string source = """
            internal static class Identities { internal const string Status = "keyload.status.snapshot"; }
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int AdmissionCapacity { get; set; } }
            [Orleans.GenerateSerializer, Orleans.Alias(Identities.Status)]
            internal sealed record Subject([property: Orleans.Id(0)] [|Policy|] Limits)
            {
                [Orleans.Id(1)] public int Capacity => Limits.AdmissionCapacity;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
