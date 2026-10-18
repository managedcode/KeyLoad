namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Exercises the production image-tooling pure contracts through real Node modules.</summary>
internal sealed class ImageToolingContractTests
{
    private const string ContextOperation = "context";
    private const string SourceOperation = "source";
    private const string EndpointOperation = "endpoint";
    private const string Linux = "linux";
    private const string SourceSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherSourceSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string SecretSentinel = "private-runner-token-do-not-print";
    private const string RunId = "37070000000";
    private const string RunAttempt = "2";
    private const string Repository = "managedcode/KeyLoad";
    private const string Ref = "refs/heads/main";
    private const string UnixEndpoint = "unix:///var/run/docker.sock";
    private const string RemoteEndpoint = "tcp://docker.example.invalid:2376";
    private const string ValidEnvironmentSha = "GITHUB_SHA";
    private const string ValidEnvironmentRunId = "GITHUB_RUN_ID";
    private const string ValidEnvironmentAttempt = "GITHUB_RUN_ATTEMPT";
    private const string ValidEnvironmentRepository = "GITHUB_REPOSITORY";
    private const string ValidEnvironmentRef = "GITHUB_REF";
    private const string ValidEnvironmentTemp = "RUNNER_TEMP";
    private const string ValidEnvironmentWorkspace = "GITHUB_WORKSPACE";
    private const string ValidEnvironmentOutput = "GITHUB_OUTPUT";
    private const string RunIdProperty = "runId";
    private const string RunAttemptProperty = "runAttempt";
    private const string RejectedContextMessage = "Required GitHub image preparation context is invalid.";
    private const string RejectedPlatformMessage = "Image preparation requires a Linux runner.";
    private const string RejectedSourceMessage = "Checked-out source does not match GITHUB_SHA.";
    private const string RejectedDirtyMessage = "Tracked source changes are not allowed for image preparation.";
    private const string RejectedEndpointMessage = "Image preparation requires the local Unix Docker Engine.";
    private const int CleanDiffExitCode = 0;
    private const int DirtyDiffExitCode = 1;

    [Test]
    public async Task AC_IMAGE_001_ContextRejectsUnsafeInputsWithoutEchoingSecrets()
    {
        var valid = await RunAsync(new
        {
            operation = ContextOperation,
            environment = ValidEnvironment(),
            platform = Linux
        });
        await Assert.That(valid.Succeeded).IsTrue();
        await Assert.That(valid.Value.GetProperty(RunIdProperty).GetString()).IsEqualTo(RunId);
        await Assert.That(valid.Value.GetProperty(RunAttemptProperty).GetString()).IsEqualTo(RunAttempt);

        var unsafeEnvironment = ValidEnvironment();
        unsafeEnvironment[ValidEnvironmentSha] = SecretSentinel;
        var rejected = await RunAsync(new
        {
            operation = ContextOperation,
            environment = unsafeEnvironment,
            platform = Linux
        });
        await Assert.That(rejected.Succeeded).IsFalse();
        await Assert.That(rejected.Message).IsEqualTo(RejectedContextMessage);
        await Assert.That(rejected.Message.Contains(SecretSentinel, StringComparison.Ordinal)).IsFalse();

        var unsafeRunId = ValidEnvironment();
        unsafeRunId[ValidEnvironmentRunId] = SecretSentinel;
        var rejectedRunId = await RunAsync(new
        {
            operation = ContextOperation,
            environment = unsafeRunId,
            platform = Linux
        });
        await Assert.That(rejectedRunId.Succeeded).IsFalse();
        await Assert.That(rejectedRunId.Message).IsEqualTo(RejectedContextMessage);
        await Assert.That(rejectedRunId.Message.Contains(SecretSentinel, StringComparison.Ordinal)).IsFalse();

        var unsupportedPlatform = await RunAsync(new
        {
            operation = ContextOperation,
            environment = ValidEnvironment(),
            platform = "win32",
        });
        await Assert.That(unsupportedPlatform.Succeeded).IsFalse();
        await Assert.That(unsupportedPlatform.Message).IsEqualTo(RejectedPlatformMessage);

    }

    [Test]
    public async Task AC_IMAGE_001_SourceRevisionMustMatchAndBeClean()
    {
        var cleanSource = await RunAsync(new
        {
            operation = SourceOperation,
            environment = ValidEnvironment(),
            platform = Linux,
            head = SourceSha,
            diffExitCode = CleanDiffExitCode,
            status = string.Empty,
        });
        await Assert.That(cleanSource.Succeeded).IsTrue();
        var mismatch = await RunAsync(new
        {
            operation = SourceOperation,
            environment = ValidEnvironment(),
            platform = Linux,
            head = OtherSourceSha,
            diffExitCode = CleanDiffExitCode,
            status = string.Empty,
        });
        await Assert.That(mismatch.Succeeded).IsFalse();
        await Assert.That(mismatch.Message).IsEqualTo(RejectedSourceMessage);

        var dirty = await RunAsync(new
        {
            operation = SourceOperation,
            environment = ValidEnvironment(),
            platform = Linux,
            head = SourceSha,
            diffExitCode = DirtyDiffExitCode,
            status = string.Empty,
        });
        await Assert.That(dirty.Succeeded).IsFalse();
        await Assert.That(dirty.Message).IsEqualTo(RejectedDirtyMessage);

        var dirtyStatus = await RunAsync(new
        {
            operation = SourceOperation,
            environment = ValidEnvironment(),
            platform = Linux,
            head = SourceSha,
            diffExitCode = CleanDiffExitCode,
            status = " M Directory.Build.props",
        });
        await Assert.That(dirtyStatus.Succeeded).IsFalse();
        await Assert.That(dirtyStatus.Message).IsEqualTo(RejectedDirtyMessage);
    }

    [Test]
    public async Task AC_IMAGE_001_OnlyMatchingLocalUnixEngineEndpointIsAccepted()
    {
        var local = await RunAsync(new
        {
            operation = EndpointOperation,
            endpoint = UnixEndpoint,
            dockerHost = string.Empty
        });
        await Assert.That(local.Succeeded).IsTrue();

        var remote = await RunAsync(new
        {
            operation = EndpointOperation,
            endpoint = RemoteEndpoint,
            dockerHost = string.Empty
        });
        await Assert.That(remote.Succeeded).IsFalse();
        await Assert.That(remote.Message).IsEqualTo(RejectedEndpointMessage);

        var mismatchedOverride = await RunAsync(new
        {
            operation = EndpointOperation,
            endpoint = UnixEndpoint,
            dockerHost = "unix:///tmp/other-docker.sock",
        });
        await Assert.That(mismatchedOverride.Succeeded).IsFalse();
        await Assert.That(mismatchedOverride.Message).IsEqualTo(RejectedEndpointMessage);
    }

    private static Dictionary<string, string> ValidEnvironment() => new(StringComparer.Ordinal)
    {
        [ValidEnvironmentSha] = SourceSha,
        [ValidEnvironmentRunId] = RunId,
        [ValidEnvironmentAttempt] = RunAttempt,
        [ValidEnvironmentRepository] = Repository,
        [ValidEnvironmentRef] = Ref,
        [ValidEnvironmentTemp] = "/tmp/runner-temp",
        [ValidEnvironmentWorkspace] = "/workspace/KeyLoad",
        [ValidEnvironmentOutput] = "/tmp/github-output",
    };

    private static Task<ImageToolingContractNodeResult> RunAsync(object request)
        => ImageToolingContractNodeProcess.RunAsync(request, TestContext.Current!.Execution.CancellationToken);
}
