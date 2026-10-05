namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-033/037: every runtime numeric context retains exact literal spans.</summary>
internal sealed class GeneralRuntimeNumericTests
{
    [Test]
    [Arguments("return [|0|] + [|1|] - [|1|];")]
    [Arguments("return -[|2147483648|];")]
    [Arguments("return new byte[[|32|]];")]
    [Arguments("return (byte)[|0xFFu|];")]
    [Arguments("return [|1.5m|] + [|2e3m|];")]
    [Arguments("return [|1.5f|] + [|2e3d|];")]
    [Arguments("var total = [|0|]; for (var index = [|0|]; index < count; index++) total += [|1|]; return total;")]
    public async Task ArithmeticAllocationLoopsAndConversionsRequireNamedValuesAsync(string operation)
    {
        var source = $$"""
            internal static class Subject
            {
                internal static object Execute(int count) { {{operation}} }
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task InitializerAndOptionalParameterValuesAreExecutionPolicyAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                private readonly int capacity = [|16|];
                internal int Limit { get; } = [|64|];
                internal int Execute(int count = [|1|]) => count + capacity + Limit;
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task ConstDeclarationsEnumsAndActualSerializerMetadataPreserveValuesAsync()
    {
        const string source = """
            [Orleans.GenerateSerializer]
            internal sealed class Subject
            {
                private const int FirstIndex = 0;
                [Orleans.Id(1)] public int Value { get; set; }
                internal enum Mode { First = 0, Second = 1 }
                internal int Execute()
                {
                    const int Step = 1;
                    return Value + FirstIndex + Step + (int)Mode.Second;
                }
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task ActualMarkedOptionsDefinitionOwnsDefaultValuesAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class OperationSettings
            {
                internal int RetryLimit { get; set; } = 3;
                internal System.TimeSpan Deadline { get; set; } = System.TimeSpan.FromSeconds(15);
                internal int AdmissionLimit;
                internal OperationSettings() { AdmissionLimit = 8; }
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
