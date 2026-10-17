using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Assertions;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

internal sealed class ProductionSourceManifestScenario : IDisposable
{
    private const string TaskAdmissionRootVariable = "KEYLOAD_TASK_ADMISSION_ROOT";
    private const string TaskAdmissionCommand = """
        $ErrorActionPreference = 'Stop'
        $root = $env:KEYLOAD_TASK_ADMISSION_ROOT
        . (Join-Path $root 'scripts/Features/CodeQuality/functional-coverage.shared.ps1')
        foreach ($name in @('task-acceptance.ps1','task-acceptance.verify.ps1','task-acceptance.contract.json')) {
            if (-not (Test-FcTaskAcceptanceScriptName $name)) { throw 'Canonical task script rejected.' }
        }
        foreach ($name in @('task-acceptance.kl065-binding.ps1','task-acceptance.unreviewed.ps1')) {
            $rejected = $false
            try { $null = Test-FcTaskAcceptanceScriptName $name }
            catch { if ($_.Exception.Message -cne $script:FunctionalCoverage.ErrorInventory) { throw }; $rejected = $true }
            if (-not $rejected) { throw 'Retired or unknown task script admitted.' }
        }
        $path = Join-Path $root 'scripts/Features/CodeQuality/task-acceptance.contract.json'
        $registry = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $task = @($registry.tasks | Where-Object { $_.taskId -ceq 'KL-065' })
        if ($task.Count -ne 1 -or $task[0].Contains('censusSelections') -or $task[0].selections.Count -eq 0) {
            throw 'KL065 must have genuine strict selections.'
        }
        foreach ($selection in $task[0].selections) {
            if ($selection.cases.Count -eq 0) { throw 'Strict task selection has no original cases.' }
        }
        """;
    private const string ContributorsProperty = "contributors";
    private const string SuiteProperty = "suite";
    private const string UnsupportedScalarSuite = "unit-scalar";
    private const string Prepare = "prepare";
    private const string Verify = "verify";
    private const string ManifestFile = "functional-coverage.production-source-manifest.json";
    private const string SettingsFile = "functional-coverage.production.settings.xml";
    private const int SuccessfulExitCode = 0;
    private const int FinalJsonPayloadOffset = 2;

    private readonly string evidenceRoot = CreateEvidenceRoot();
    private readonly Microsoft.Extensions.Options.IOptions<TestExecutionOptions> executionOptions =
        ProductionSourceManifestProcess.CaptureExecutionOptions();

    internal static async Task RunOwnedAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        ProductionSourceManifestScenario? scenario = null;
        ServerFailureObserver.Observe(() => scenario = new ProductionSourceManifestScenario(), failures);
        if (scenario is not null)
        {
            await ServerFailureObserver.ObserveAsync(
                () => scenario.VerifyCreateTamperAndFollowupAsync(cancellationToken), failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(scenario.Dispose, failures);
        }
        global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
    }

    public void Dispose() => Directory.Delete(evidenceRoot, recursive: true);

    private static string CreateEvidenceRoot()
    {
        var temporary = Path.GetFullPath(Path.GetTempPath());
        var current = Path.GetPathRoot(temporary)!;
        foreach (var segment in temporary.Substring(current.Length).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, segment);
            current = new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
        }
        var root = Path.Combine(current, "keyload-source-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private async Task VerifyCreateTamperAndFollowupAsync(CancellationToken cancellationToken)
    {
        await VerifyTaskAdmissionAsync(cancellationToken);
        var prepared = await ProductionSourceManifestProcess.RunAsync(executionOptions, Prepare, evidenceRoot, cancellationToken);
        await AssertSuccessfulProcessAsync(prepared);
        await ProductionSourceManifestAssertions.AssertManifestAsync(evidenceRoot);
        await NativeUnitImageAdmissionFlow.VerifyAsync(evidenceRoot, executionOptions, cancellationToken);
        await NativeFunctionalReportAdmissionFlow.VerifyAsync(evidenceRoot, executionOptions, cancellationToken);
        await NativeCoverageRf3ContributorAdmissionFlow.VerifyAsync(Path.Combine(evidenceRoot, ManifestFile), cancellationToken);
        await VerifyUnsupportedScalarRejectedAsync(cancellationToken);
        await VerifyTamperRejectedAsync(ManifestFile, cancellationToken);
        await VerifyTamperRejectedAsync("functional-coverage.test-image.recovery.json", cancellationToken);
        await VerifyTamperRejectedAsync(SettingsFile, cancellationToken);
        await VerifyRestoredAcceptedAsync(cancellationToken);
        await VerifyCreateOnlyAsync(cancellationToken);
    }

    private async Task VerifyTaskAdmissionAsync(CancellationToken cancellationToken)
    {
        var root = ProductionSourceManifestProcess.RepositoryRoot;
        var registry = Path.Combine(root, "scripts/Features/CodeQuality/task-acceptance.contract.json");
        var original = await File.ReadAllBytesAsync(registry, cancellationToken);
        var start = new global::System.Diagnostics.ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(TaskAdmissionCommand);
        start.Environment[TaskAdmissionRootVariable] = root;
        var result = await ProductionSourceManifestProcess.RunAsync(executionOptions, start, cancellationToken);
        await AssertSuccessfulProcessAsync(result);
        await Assert.That(await File.ReadAllBytesAsync(registry, cancellationToken)).IsEquivalentTo(
            original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private async Task VerifyUnsupportedScalarRejectedAsync(CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(evidenceRoot, ManifestFile);
        var original = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var changed = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
        changed[ContributorsProperty]!.AsArray()[0]![SuiteProperty] = UnsupportedScalarSuite;
        var altered = System.Text.Encoding.UTF8.GetBytes(changed.ToJsonString());
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(manifestPath, altered, cancellationToken);
            var rejected = await ProductionSourceManifestProcess.RunAsync(executionOptions, Verify, evidenceRoot, cancellationToken);
            await Assert.That(rejected.ExitCode).IsNotEqualTo(SuccessfulExitCode);
            await AssertProcessSettledAsync(rejected);
            await Assert.That(await File.ReadAllBytesAsync(manifestPath, cancellationToken)).IsEquivalentTo(
                altered, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(
            () => File.WriteAllBytesAsync(manifestPath, original, CancellationToken.None), failures).ConfigureAwait(false);
        global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
        await VerifyRestoredAcceptedAsync(cancellationToken);
    }

    private async Task VerifyTamperRejectedAsync(string fileName, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(evidenceRoot, fileName);
        var original = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var altered = (byte[])original.Clone();
        altered[altered.Length - FinalJsonPayloadOffset] = altered[altered.Length - FinalJsonPayloadOffset] == (byte)' ' ? (byte)'\n' : (byte)' ';
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(manifestPath, altered, cancellationToken);
            var rejected = await ProductionSourceManifestProcess.RunAsync(executionOptions, Verify, evidenceRoot, cancellationToken);
            await Assert.That(rejected.ExitCode).IsNotEqualTo(SuccessfulExitCode);
            await AssertProcessSettledAsync(rejected);
            await Assert.That(await File.ReadAllBytesAsync(manifestPath, cancellationToken)).IsEquivalentTo(altered, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(
            () => File.WriteAllBytesAsync(manifestPath, original, CancellationToken.None), failures).ConfigureAwait(false);
        global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
    }

    private async Task VerifyRestoredAcceptedAsync(CancellationToken cancellationToken)
    {
        var result = await ProductionSourceManifestProcess.RunAsync(executionOptions, Verify, evidenceRoot, cancellationToken);
        await AssertSuccessfulProcessAsync(result);
        await ProductionSourceManifestAssertions.AssertManifestAsync(evidenceRoot);
        await NativeCoverageRf3ContributorAdmissionFlow.VerifyAsync(Path.Combine(evidenceRoot, ManifestFile), cancellationToken);
    }

    private static async Task AssertSuccessfulProcessAsync(ProductionSourceManifestProcessResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(SuccessfulExitCode).Because(result.StandardError);
        await AssertProcessSettledAsync(result);
    }

    private static async Task AssertProcessSettledAsync(ProductionSourceManifestProcessResult result)
    {
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
    }

    private async Task VerifyCreateOnlyAsync(CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(evidenceRoot, ManifestFile);
        var original = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var imagePath = Path.Combine(evidenceRoot, "functional-coverage.test-image.unit.json");
        var originalImage = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var settingsPath = Path.Combine(evidenceRoot, SettingsFile);
        var originalSettings = await File.ReadAllBytesAsync(settingsPath, cancellationToken);
        var rejected = await ProductionSourceManifestProcess.RunAsync(executionOptions, Prepare, evidenceRoot, cancellationToken);
        await Assert.That(rejected.ExitCode).IsNotEqualTo(SuccessfulExitCode);
        await AssertProcessSettledAsync(rejected);
        await Assert.That(await File.ReadAllBytesAsync(manifestPath, cancellationToken)).IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(imagePath, cancellationToken)).IsEquivalentTo(originalImage, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(settingsPath, cancellationToken)).IsEquivalentTo(originalSettings, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
