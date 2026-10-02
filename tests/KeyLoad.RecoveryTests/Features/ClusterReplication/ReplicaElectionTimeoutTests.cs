using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-003: real cryptographic election sampling retains exact inclusive/exclusive tick bounds.</summary>
internal sealed class ReplicaElectionTimeoutTests
{
    private const string VoterA = "election-voter-a";
    private const string VoterB = "election-voter-b";
    private const string VoterC = "election-voter-c";
    private const int SampleCount = 64;
    private const int LowerTimeoutSeconds = 4;
    private const long OneTick = 1;
    private const long ThreeTicks = 3;
    private const long PowerOfTwoTicks = 16;
    private const long AbovePowerOfTwoTicks = 17;
    private static readonly TimeSpan LowerTimeout = TimeSpan.FromSeconds(LowerTimeoutSeconds);

    /// <summary>Valid narrow intervals preserve tick precision, including the unique one-tick result.</summary>
    /// <param name="widthTicks">Positive width of the validated election interval in ticks.</param>
    [Test]
    [Arguments(OneTick)]
    [Arguments(ThreeTicks)]
    [Arguments(PowerOfTwoTicks)]
    [Arguments(AbovePowerOfTwoTicks)]
    public async Task NarrowIntervalsRetainTheirExactTickBounds(long widthTicks)
    {
        var configuration = Configuration(LowerTimeout + TimeSpan.FromTicks(widthTicks));
        await AssertSamplesAsync(configuration);
    }

    /// <summary>The maximum positive TimeSpan endpoint retains exclusive upper bounds without narrowing or overflow.</summary>
    [Test]
    public async Task MaximumPositiveIntervalRetainsItsExactTickBounds()
    {
        var configuration = Configuration(TimeSpan.MaxValue);
        await AssertSamplesAsync(configuration);
    }

    private static ReplicaConfiguration Configuration(TimeSpan upperTimeout)
    {
        var configuration = new ReplicaConfiguration(VoterA, [VoterA, VoterB, VoterC], Path.GetTempPath(), Guid.NewGuid())
        {
            LowerElectionTimeout = LowerTimeout,
            UpperElectionTimeout = upperTimeout
        };
        configuration.Validate();
        return configuration;
    }

    private static async Task AssertSamplesAsync(ReplicaConfiguration configuration)
    {
        var lowerTicks = configuration.LowerElectionTimeout.Ticks;
        var upperTicks = configuration.UpperElectionTimeout.Ticks;
        for (var sample = 0; sample < SampleCount; sample++)
        {
            var selected = ReplicaElectionTimeout.Select(configuration);
            await Assert.That(selected.Ticks).IsGreaterThanOrEqualTo(lowerTicks);
            await Assert.That(selected.Ticks).IsLessThan(upperTicks);
            if (upperTicks - lowerTicks == OneTick)
            {
                await Assert.That(selected).IsEqualTo(configuration.LowerElectionTimeout);
            }
        }
    }
}
