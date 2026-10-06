using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ServerOnlyImagePreparationTests
{
    private const string ServerField = "server";
    private const string ComparisonField = "comparison";
    private const string RejectedField = "rejected";
    private const string OutputField = "output";
    private const string Probe = """
        import { pathToFileURL } from 'node:url';
        import { mkdtemp, writeFile, readFile, rm } from 'node:fs/promises';
        import path from 'node:path';
        import os from 'node:os';
        const [preparationModule, evidenceModule] = process.argv.slice(1);
        process.argv = [];
        const api = await import(pathToFileURL(preparationModule).href);
        const server = api.preparationImageKinds(['--server-only']);
        const comparison = api.preparationImageKinds([]);
        let rejected = 0;
        for (const args of [['--unknown'], ['--server-only', '--server-only'], ['--server-only', '--unknown']]) {
          try { api.preparationImageKinds(args); } catch { rejected++; }
        }
        const evidence = await import(pathToFileURL(evidenceModule).href);
        const directory = await mkdtemp(path.join(os.tmpdir(), 'keyload-server-output-'));
        try {
          const output = path.join(directory, 'output');
          await writeFile(output, '');
          await evidence.appendImageOutputs({githubOutput: output}, 'server-reference');
          process.stdout.write(JSON.stringify({server, comparison, rejected, output: await readFile(output, 'utf8')}));
        } finally { await rm(directory, {recursive: true, force: true}); }
        """;

    [Test]
    public async Task ServerOnlySelectionExcludesLoadGeneratorAndRejectsAmbiguousModes()
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", Probe, IsolatedAggregateNodeProcess.Module("prepare-images.mjs"),
                IsolatedAggregateNodeProcess.Module("image-evidence.mjs")],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        using var report = JsonDocument.Parse(result.Output);
        await Assert.That(report.RootElement.GetProperty(ServerField).EnumerateArray()
            .Select(value => value.GetString()!).ToArray()).IsEquivalentTo(["server"]);
        await Assert.That(report.RootElement.GetProperty(ComparisonField).GetArrayLength()).IsEqualTo(2);
        await Assert.That(report.RootElement.GetProperty(RejectedField).GetInt32()).IsEqualTo(3);
        await Assert.That(report.RootElement.GetProperty(OutputField).GetString()).IsEqualTo("server-image=server-reference\n");
    }
}
