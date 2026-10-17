namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-034/037: native admission, channel and transport bounds use configured policy.</summary>
internal sealed class TypedConfigurationCapacityTests
{
    [Test]
    public async Task HiddenConstSemaphoreAndChannelCapacitiesRemainOperationalPolicyAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int AdmissionCapacity = 64;
                internal static void Execute()
                {
                    using var gate = [|new System.Threading.SemaphoreSlim(AdmissionCapacity, AdmissionCapacity)|];
                    var channel = [|System.Threading.Channels.Channel.CreateBounded<int>(AdmissionCapacity)|];
                    var channelOptions = [|new System.Threading.Channels.BoundedChannelOptions(AdmissionCapacity)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ExactSinglePermitMutexIdentityIsStructuralAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int MutexPermits = 1;
                internal static void Execute()
                {
                    using var gate = new System.Threading.SemaphoreSlim(MutexPermits, MutexPermits);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task NativeHttpTimeoutAndConnectionCapacityConstAssignmentsAreRejectedAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int MaximumConnections = 64;
                private const int TimeoutSeconds = 30;
                private static readonly System.TimeSpan Deadline = [|System.TimeSpan.FromSeconds(TimeoutSeconds)|];
                internal static void Execute(System.Net.Http.HttpClient client, System.Net.Http.SocketsHttpHandler handler)
                {
                    [|handler.MaxConnectionsPerServer = MaximumConnections|];
                    [|handler.ConnectTimeout = Deadline|];
                    [|client.Timeout = Deadline|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ActualOptionsValuesReachNativeAdmissionAndTransportOperationsAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy
            {
                public int AdmissionCapacity { get; set; } = 64;
                public System.TimeSpan Deadline { get; set; } = System.TimeSpan.FromSeconds(30);
            }
            internal static class Subject
            {
                internal static void Execute(Microsoft.Extensions.Options.IOptions<Policy> configured,
                    System.Net.Http.HttpClient client, System.Net.Http.SocketsHttpHandler handler)
                {
                    var policy = configured.Value;
                    using var gate = new System.Threading.SemaphoreSlim(policy.AdmissionCapacity, policy.AdmissionCapacity);
                    System.Threading.Channels.Channel.CreateBounded<int>(policy.AdmissionCapacity);
                    handler.MaxConnectionsPerServer = policy.AdmissionCapacity;
                    handler.ConnectTimeout = policy.Deadline;
                    client.Timeout = policy.Deadline;
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
        await MagicRuntimeFixture.AssertDurationAsync(source);
    }
}
