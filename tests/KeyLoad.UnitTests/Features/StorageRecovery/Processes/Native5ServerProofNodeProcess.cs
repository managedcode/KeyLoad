using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record Native5ServerProofProbeResult(bool Accepted, string Name);

internal static class Native5ServerProofNodeProcess
{
    private const string Probe = """
        import { pathToFileURL } from 'node:url';
        import { createHash } from 'node:crypto';
        const module = await import(pathToFileURL(process.argv[1]).href);
        const cases = JSON.parse(process.argv[2]);
        const results = [];
        for (const item of cases) {
          try {
            let manifest = item.oversizedManifest ? Buffer.alloc(1024 * 1024 + 1)
              : Buffer.from(item.manifest, 'base64');
            let receipt = Buffer.from(item.receipt, 'base64');
            let reference = item.reference;
            if (item.layerCountOverride > 0) {
              const imageReceipt = JSON.parse(receipt.toString('utf8'));
              const manifestDocument = JSON.parse(manifest.toString('utf8'));
              manifestDocument.layers = Array.from({ length: item.layerCountOverride },
                () => ({ ...manifestDocument.layers[0] }));
              manifest = Buffer.from(JSON.stringify(manifestDocument));
              const digest = `sha256:${createHash('sha256').update(manifest).digest('hex')}`;
              imageReceipt.image.manifestSha256 = digest;
              imageReceipt.image.registryDigest = digest;
              reference = `${imageReceipt.image.taggedReference}@${digest}`;
              imageReceipt.image.reference = reference;
              receipt = Buffer.from(JSON.stringify(imageReceipt));
            }
            module.parseNative5ServerProof(receipt, manifest, item.producer, reference);
            results.push({ Name: item.name, Accepted: true });
          } catch {
            results.push({ Name: item.name, Accepted: false });
          }
        }
        process.stdout.write(JSON.stringify(results));
        """;

    internal static async Task<IReadOnlyList<Native5ServerProofProbeResult>> ProbeAsync(
        IReadOnlyList<Native5ServerParserCase> cases)
    {
        var allResults = new List<Native5ServerProofProbeResult>();
        var batch = new List<Native5ServerParserCase>();
        foreach (var item in cases)
        {
            batch.Add(item);
            var serialized = Serialize(batch);
            if (serialized.Length <= MaximumInputCharacters)
            { continue; }
            batch.RemoveAt(batch.Count - 1);
            if (batch.Count == 0)
            { throw new InvalidOperationException(ProcessFailure); }
            allResults.AddRange(await ProbeBatchAsync(batch));
            batch.Clear();
            batch.Add(item);
        }
        if (batch.Count > 0)
        { allResults.AddRange(await ProbeBatchAsync(batch)); }
        return allResults;
    }

    private static async Task<IReadOnlyList<Native5ServerProofProbeResult>> ProbeBatchAsync(
        IReadOnlyList<Native5ServerParserCase> cases)
    {
        var serialized = Serialize(cases);
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", Probe, ModulePath(), serialized],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEmpty();
        return JsonSerializer.Deserialize<Native5ServerProofProbeResult[]>(result.Output)
            ?? throw new InvalidOperationException(ProcessFailure);
    }

    private static string Serialize(IEnumerable<Native5ServerParserCase> cases)
    {
        var payload = cases.Select(item => new
        {
            name = item.Name,
            receipt = Convert.ToBase64String(Encoding.UTF8.GetBytes(item.Receipt)),
            manifest = Convert.ToBase64String(Encoding.UTF8.GetBytes(item.Manifest)),
            producer = JsonNode.Parse(item.ExpectedProducer),
            reference = item.ExpectedReference,
            oversizedManifest = item.OversizedManifest,
            layerCountOverride = item.LayerCountOverride
        });
        return JsonSerializer.Serialize(payload);
    }

    private static string ModulePath()
        => Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), "scripts", "Features", "StorageRecovery",
            "native5-server-proof.mjs");

    private const int MaximumInputCharacters = 96 * 1024;
    private const string ProcessFailure = "The native5 server proof parser child returned an invalid response.";
}
