namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-037: a genuine temporal-data field never grants execution policy.</summary>
internal sealed class ImmutableTemporalDataTests
{
    [Test]
    public async Task GenuineStaticReadonlyTemporalFieldsPreserveConstantCorpusIdentityAsync()
    {
        const string source = """
            internal static class Corpus
            {
                private const int BucketMinutes = 5;
                private const long OffsetTicks = System.TimeSpan.TicksPerHour;
                [KeyLoad.ImmutableTemporalData]
                internal static readonly System.TimeSpan BucketWidth = System.TimeSpan.FromMinutes(BucketMinutes);
                [KeyLoad.ImmutableTemporalData]
                internal static readonly System.TimeSpan TimestampOffset = new System.TimeSpan(OffsetTicks);
                internal static System.DateTimeOffset Represent(System.DateTimeOffset timestamp) =>
                    timestamp.ToOffset(TimestampOffset);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task CounterfeitTemporalMarkerDoesNotGrantFieldOrMethodOwnershipAsync()
    {
        const string source = """
            namespace Counterfeit
            {
                internal sealed class ImmutableTemporalDataAttribute : System.Attribute { }
                internal static class Corpus
                {
                    private const int BucketMinutes = 5;
                    [ImmutableTemporalData]
                    internal static readonly System.TimeSpan BucketWidth = [|System.TimeSpan.FromMinutes(BucketMinutes)|];
                    [ImmutableTemporalData]
                    internal static System.TimeSpan Build() => [|System.TimeSpan.FromMinutes(BucketMinutes)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task RealMarkerRequiresStaticReadonlyNativeTimeSpanFieldAsync()
    {
        const string source = """
            internal sealed class Corpus
            {
                private const int BucketMinutes = 5;
                [KeyLoad.ImmutableTemporalData]
                internal static System.TimeSpan Mutable = [|System.TimeSpan.FromMinutes(BucketMinutes)|];
                [KeyLoad.ImmutableTemporalData]
                internal readonly System.TimeSpan Instance = [|System.TimeSpan.FromMinutes(BucketMinutes)|];
                [KeyLoad.ImmutableTemporalData]
                internal static readonly System.TimeSpan? Nullable = [|System.TimeSpan.FromMinutes(BucketMinutes)|];
                [KeyLoad.ImmutableTemporalData]
                internal static readonly object Boxed = [|System.TimeSpan.FromMinutes(BucketMinutes)|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task MarkedTemporalDataUsedAsDeadlineRemainsHardcodedAtEveryNativeSinkAsync()
    {
        const string source = """
            internal static class Corpus
            {
                private const int BucketMinutes = 5;
                [KeyLoad.ImmutableTemporalData]
                internal static readonly System.TimeSpan BucketWidth = System.TimeSpan.FromMinutes(BucketMinutes);
                internal static void Execute(System.Net.Http.HttpClient client, System.Threading.SemaphoreSlim gate)
                {
                    [|System.Threading.Tasks.Task.Delay(BucketWidth)|];
                    using var cancellation = [|new System.Threading.CancellationTokenSource(BucketWidth)|];
                    [|cancellation.CancelAfter(BucketWidth)|];
                    [|client.Timeout = BucketWidth|];
                    [|gate.WaitAsync(BucketWidth)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
