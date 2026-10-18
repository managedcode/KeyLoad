namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-037: persisted caller policy and frozen request defaults remain serialized data.</summary>
internal sealed class PersistedPolicyLiteralTests
{
    [Test]
    public async Task RealSerializerContractsOwnNamedProtocolDefaultsAsync()
    {
        const string source = """
            internal static class Aliases
            {
                internal const string Subscription = "keyload.subscription-policy";
                internal const string ReadRequest = "keyload.read-request";
            }
            [Orleans.GenerateSerializer, Orleans.Alias(Aliases.Subscription)]
            internal sealed record SubscriptionPolicy
            {
                private const int DefaultMaxAttempts = 5;
                private const int DefaultRetryBaseMilliseconds = 1_000;
                [Orleans.Id(0)] public int MaxAttempts { get; init; } = DefaultMaxAttempts;
                [Orleans.Id(1)] public int RetryBaseMilliseconds { get; init; } = DefaultRetryBaseMilliseconds;
            }
            [Orleans.GenerateSerializer, Orleans.Alias(Aliases.ReadRequest)]
            internal sealed record ReadRequest([property: Orleans.Id(0)] int Limit = ReadRequest.DefaultLimit)
            {
                private const int DefaultLimit = 100;
            }
            internal static class Subject
            {
                internal static SubscriptionPolicy Receive(SubscriptionPolicy persisted) => persisted;
                internal static ReadRequest Create() => new();
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
        await MagicRuntimeFixture.AssertDurationAsync(source);
        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task RealSerializerMetadataDoesNotGrantMutableRuntimeOptionsInjectionAsync()
    {
        const string source = """
            internal static class Aliases { internal const string Subject = "keyload.runtime-owner"; }
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Capacity { get; set; } = 64; }
            [Orleans.GenerateSerializer, Orleans.Alias(Aliases.Subject)]
            internal sealed class Subject
            {
                [Orleans.Id(0)] public [|Policy|] Settings { get; set; }
                internal Subject([|Policy|] settings) => Settings = settings;
                internal int Execute() => Settings.Capacity;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
