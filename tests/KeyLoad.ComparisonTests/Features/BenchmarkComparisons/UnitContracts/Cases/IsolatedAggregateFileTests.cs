using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-007 retains exact source bytes while bounding real file input.</summary>
internal sealed class IsolatedAggregateFileTests
{
    private const string ModuleMode = "--input-type=module";
    private const string EvaluationMode = "-e";
    private const string Module = "aggregate-files.mjs";
    private const string Prefix = "keyload-isolated-bytes-";
    private const string InputFile = "input.json";
    private const string Cell = "keyload-n1-point-read";
    private const string Stage = "stage";
    private const string Workers = "workers";
    private const string WorkerFile = "worker.json";
    private const string Content = "{\n  \"contract\": [1, 2, 3]\n}\n";
    private const string Exact = "exact";
    private const string Bound = "bounded";
    private const string Probe = """
        import {pathToFileURL} from 'node:url';
        const module=await import(pathToFileURL(process.argv[3]).href);
        const bytes=await module.readBytes(process.argv[1],65536);
        await module.retainBytes(process.argv[2],'keyload-n1-point-read',bytes);
        const copied=await module.readBytes(module.rawFile(process.argv[2],'keyload-n1-point-read'),65536);
        let bounded=false;try{await module.readBytes(process.argv[1],1);}catch{bounded=true;}
        process.stdout.write(JSON.stringify({exact:bytes.equals(copied)&&module.hashBytes(bytes)===module.hashBytes(copied),bounded}));
        """;

    [Test]
    public async Task AC_ISO_007_PreservesWhitespaceAndRawHashAndRejectsOversizedInput()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, InputFile);
            var stage = Path.Combine(root, Stage);
            await File.WriteAllTextAsync(input, Content, token);
            var response = await IsolatedAggregateNodeProcess.RunAsync(
                [ModuleMode, EvaluationMode, Probe, input, stage, IsolatedAggregateNodeProcess.Module(Module)], token);
            await Assert.That(response.ExitCode).IsEqualTo(0);
            await Assert.That(response.Error).IsEqualTo(string.Empty);
            using var receipt = JsonDocument.Parse(response.Output);
            await Assert.That(receipt.RootElement.GetProperty(Exact).GetBoolean()).IsTrue();
            await Assert.That(receipt.RootElement.GetProperty(Bound).GetBoolean()).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(stage, Workers, Cell, WorkerFile), token)).IsEqualTo(Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
