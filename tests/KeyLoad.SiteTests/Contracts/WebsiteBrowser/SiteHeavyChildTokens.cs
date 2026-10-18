namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteHeavyChildTokens
{
    internal const int ActiveCapacity = 2;
    internal const int MaximumQueued = 64;
    internal const int AdmissionMinutes = 20;
    internal const string QueueFull = "The heavy site child admission queue is full.";
    internal const string UnsafeOwnership = "Heavy site child admission stopped because native ownership did not settle.";
    internal const string Operation = "operation";
    internal const string Arguments = "arguments";
    internal const string Produce = "produce";
    internal const string FixtureScript = "heavy-child.mjs";
    internal const string FixtureStarted = "started.pid";
    internal const string FixtureRelease = "release";
    internal const string FixturePredecessor = "predecessor-alive.json";
    internal const string FixtureMissingExecutable = "absent-node-executable";
    internal const string FixtureHold = "hold";
    internal const string FixtureOverflow = "overflow";
    internal const string FixtureOutput = "settled";
    internal const string FixtureAlreadyStarted = "The owned heavy-child fixture has already started.";
    internal const int FixtureDeadlineMilliseconds = 10000;
    internal const int FixturePollMilliseconds = 10;
    internal const int FixtureQueueMilliseconds = 1000;
    internal const int FixtureQueueCapacity = 3;
}
