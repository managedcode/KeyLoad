using System.Globalization;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class MongoNativeReadinessRegressionProtocol
{
    internal const string Failure = "MongoNativeReadinessRegressionFailed";
    internal const string CleanupFailure = "MongoNativeReadinessCleanupIncomplete";
    internal const string Docker = "docker";
    internal const string Bootstrap = "isolated-mongo-bootstrap";
    internal const string Prefix = "isolated-mongo-";
    internal const string ChildPrefix = "keyload-mongo-readiness-";
    internal const string GuidFormat = "N";
    internal const string ModuleTarget = "/bootstrap/isolated-mongo-readiness.js";
    internal const string FixtureTarget = "/bootstrap/mongo-native-readiness-regression.js";
    internal const string FixtureDirectory = "tests/KeyLoad.ComparisonTests/Features/BenchmarkComparisons/Processes";
    internal const string FixtureFile = "MongoNativeReadinessRegression.js";
    internal const string Solution = "KeyLoad.slnx";
    internal const string UserEnvironment = "MONGO_INITDB_ROOT_USERNAME";
    internal const string PasswordEnvironment = "MONGO_INITDB_ROOT_PASSWORD";
    internal const string MembersEnvironment = "KEYLOAD_MONGO_MEMBERS";
    internal const string Username = "benchmark";
    internal const string Port = ":27017";
    internal const string SourceEnvironment = "GITHUB_SHA";
    internal const string RunEnvironment = "GITHUB_RUN_ID";
    internal const string AttemptEnvironment = "GITHUB_RUN_ATTEMPT";
    internal const string SourceLabel = "keyload.source";
    internal const string RunLabel = "keyload.run";
    internal const string AttemptLabel = "keyload.attempt";
    internal const string TaskLabel = "keyload.task";
    internal const string FixtureLabel = "keyload.fixture";
    internal const string Task = "TASK-ISO-031M-N";
    internal const string DomainPassed = "MongoNativeReadinessDomainPassed";
    internal const string AdmissionPassed = "MongoNativeReadinessAdmissionPassed";
    internal const string LowerObserved = "MongoNativeReadinessLowerPrimaryObserved";
    internal const string ElectionPassed = "MongoNativeReadinessElectionPassed";
    internal const int MinimumNodes = 1, MaximumNodes = 3, SourceCharacters = 40, ContainerCharacters = 64;
    internal const int ShortContainerCharacters = 12, OutputCharacters = 32_768, ErrorCharacters = 4_096;
    internal const int IdentifierCharacters = 128, CommonNetworkCount = 1;
    internal const int JsonDepth = 12, DigestPrefixLength = 7, SuccessfulExit = 0, ExpectedMarkerCount = 1;
    internal static readonly TimeSpan ParentDeadline = TimeSpan.FromSeconds(300);
    internal static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(30);

    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException(Failure);
        }
    }

    internal static string Node(int index) => Prefix + (index + MinimumNodes).ToString(CultureInfo.InvariantCulture);

    internal static bool Identifier(string value)
        => value.Length is > 0 and <= IdentifierCharacters
            && value.All(static item => char.IsAsciiLetterOrDigit(item) || item is '-' or '_' or '.');

    internal static string Source(string file)
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, Solution)))
            {
                var path = Path.Combine(directory, FixtureDirectory, file);
                Require(File.Exists(path));
                return Path.GetFullPath(path);
            }
        }
        throw new InvalidOperationException(Failure);
    }
}
