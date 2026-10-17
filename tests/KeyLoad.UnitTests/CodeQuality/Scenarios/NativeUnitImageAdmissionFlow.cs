using System.Text.Json.Nodes;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

internal static class NativeUnitImageAdmissionFlow
{
    private const string ImageFileName = "functional-coverage.test-image.unit.json";
    private const string Qualification = "qualification";
    private const string InvalidQualification = "unqualified altered sidecar";
    private const string InvalidManifest = "The UnitTests image identity manifest is malformed, stale or unsupported.";
    private const int SuccessfulExitCode = 0;

    internal static async Task VerifyAsync(string evidenceRoot, IOptions<TestExecutionOptions> options,
        CancellationToken token)
    {
        var path = Path.Combine(evidenceRoot, ImageFileName);
        var original = await File.ReadAllBytesAsync(path, token);
        await AssertAcceptedAsync(await NativeUnitImageAdmissionReader.ReadAsync(evidenceRoot, options, token));
        var changed = JsonNode.Parse(original)!.AsObject();
        changed[Qualification] = InvalidQualification;
        var altered = System.Text.Encoding.UTF8.GetBytes(changed.ToJsonString());
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(path, altered, token);
            var rejected = await NativeUnitImageAdmissionReader.ReadAsync(evidenceRoot, options, token);
            await Assert.That(rejected.ExitCode).IsNotEqualTo(SuccessfulExitCode);
            await Assert.That(rejected.StandardError.Trim()).IsEqualTo(InvalidManifest);
            await AssertSettledAsync(rejected);
            await Assert.That((await File.ReadAllBytesAsync(path, token)).SequenceEqual(altered)).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(
            () => File.WriteAllBytesAsync(path, original, CancellationToken.None), failures);
        global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
        await AssertAcceptedAsync(await NativeUnitImageAdmissionReader.ReadAsync(evidenceRoot, options, token));
        await Assert.That((await File.ReadAllBytesAsync(path, token)).SequenceEqual(original)).IsTrue();
    }

    private static async Task AssertAcceptedAsync(ProductionSourceManifestProcessResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(SuccessfulExitCode).Because(result.StandardError);
        await AssertSettledAsync(result);
    }

    private static async Task AssertSettledAsync(ProductionSourceManifestProcessResult result)
    {
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
    }
}
