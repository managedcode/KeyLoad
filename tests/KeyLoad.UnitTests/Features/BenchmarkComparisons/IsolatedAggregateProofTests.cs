using System.Text.Json;
using F = KeyLoad.UnitTests.Features.BenchmarkComparisons.IsolatedAggregateFields;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-007 supplied-proof contract checks; fixtures do not authenticate GitHub.</summary>
internal sealed class IsolatedAggregateProofTests
{
    private const string ModuleMode = "--input-type=module";
    private const string EvaluationMode = "-e";
    private const string Module = "aggregate-validation.mjs";
    private const string Probe = """
        import {readFileSync} from 'node:fs';
        import {pathToFileURL} from 'node:url';
        const module=await import(pathToFileURL(process.argv[3]).href);
        const plan=JSON.parse(readFileSync(process.argv[1],'utf8'));
        const proof=JSON.parse(readFileSync(process.argv[2],'utf8'));
        try {module.validateAggregateProof(proof,plan);process.stdout.write('{"accepted":true}');}
        catch {process.stdout.write('{"accepted":false}');}
        """;

    [Test]
    public async Task AC_ISO_007_AcceptsCompleteDistinctJobsAndRejectsDuplicateArtifactsExpiredOrSkippedSteps()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = await IsolatedAggregateCliFixture.CreateAsync(token);
        await Assert.That(await AcceptedAsync(fixture, token)).IsTrue();
        var proof = await fixture.ReadProofAsync(token);
        var cells = proof[F.Cells]!.AsArray();
        cells[1]![F.Artifact]![F.Id] = cells[0]![F.Artifact]![F.Id]!.GetValue<long>();
        await fixture.WriteProofAsync(proof, token);
        await Assert.That(await AcceptedAsync(fixture, token)).IsFalse();
        cells[1]![F.Artifact]![F.Id] = 2001;
        cells[0]![F.Artifact]![F.Expired] = true;
        await fixture.WriteProofAsync(proof, token);
        await Assert.That(await AcceptedAsync(fixture, token)).IsFalse();
        cells[0]![F.Artifact]![F.Expired] = false;
        cells[0]![F.Job]![F.Steps]![0]![F.Conclusion] = F.Skipped;
        await fixture.WriteProofAsync(proof, token);
        await Assert.That(await AcceptedAsync(fixture, token)).IsFalse();
    }

    private static async Task<bool> AcceptedAsync(IsolatedAggregateCliFixture fixture, CancellationToken token)
    {
        var response = await IsolatedAggregateNodeProcess.RunAsync(
            [ModuleMode, EvaluationMode, Probe, fixture.Plan, fixture.Proof, IsolatedAggregateNodeProcess.Module(Module)], token);
        await Assert.That(response.ExitCode).IsEqualTo(0);
        await Assert.That(response.Error).IsEqualTo(string.Empty);
        using var result = JsonDocument.Parse(response.Output);
        return result.RootElement.GetProperty(F.Accepted).GetBoolean();
    }
}
