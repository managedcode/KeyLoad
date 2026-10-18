using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-007 retains exact source bytes while bounding real file input.</summary>
internal sealed class IsolatedAggregateFileTests
{
    private const string ModuleMode = "--input-type=module";
    private const string EvaluationMode = "-e";
    private const string Module = "aggregate-files.mjs";
    private const string Prefix = "keyload-isolated-bytes-";
    private const string InputFile = "input.json";
    private const string Stage = "stage";
    private const string Content = "{\n  \"contract\": [1, 2, 3]\n}\n";
    private const string Exact = "exact";
    private const string Bound = "bounded";
    private const string Probe = """
        import {pathToFileURL} from 'node:url';
        const module=await import(pathToFileURL(process.argv[3]).href);
        const bytes=await module.readBytes(process.argv[1],65536);
        let bounded=false;try{await module.readBytes(process.argv[1],1);}catch{bounded=true;}
        process.stdout.write(JSON.stringify({exact:module.hashBytes(bytes),bounded}));
        """;

    [Test]
    public async Task AC_ISO_007_PreservesWhitespaceAndRawHashAndRejectsOversizedInput()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var root = Path.Combine(SessionNodeProcess.RepositoryRoot(), "artifacts", "qualification", Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, InputFile);
            var stage = Path.Combine(root, Stage);
            await File.WriteAllTextAsync(input, Content, token);
            var response = await SessionNodeProcess.RunAsync(
                [ModuleMode, EvaluationMode, Probe, input, stage, SessionNodeProcess.Module(Module)], token);
            await Assert.That(response.ExitCode).IsEqualTo(0);
            await Assert.That(response.Error).IsEqualTo(string.Empty);
            using var receipt = JsonDocument.Parse(response.Output);
            await Assert.That(receipt.RootElement.GetProperty(Exact).GetString()).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(input, token))));
            await Assert.That(receipt.RootElement.GetProperty(Bound).GetBoolean()).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(input, token)).IsEqualTo(Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
