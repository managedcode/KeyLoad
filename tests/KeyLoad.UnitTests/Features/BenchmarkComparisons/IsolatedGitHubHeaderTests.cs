namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Controlled native-output parser bytes only; no HTTP client, server or provider authentication is simulated.</summary>
internal sealed class IsolatedGitHubHeaderTests
{
    private const string Source = """
        import { readFile } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, root, blockSize] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const target = path.join(root, 'headers.txt');
        const header = Buffer.from('HTTP/2.0 200 OK\nContent-Type: application/zip\r\n\r\n');
        const body = Buffer.from([0,255,128,13,10,13,10,1,2,3]);
        const bytes = Buffer.concat([header, body]);
        const decoder = api.responseDecoder(target);
        const output = [];
        for (let position = 0; position < bytes.length; position += Number(blockSize)) {
          output.push(await decoder.decode(bytes.subarray(position, position + Number(blockSize))));
        }
        if (decoder.response.status !== 200 || !Buffer.concat(output).equals(body) || !(await readFile(target)).equals(header)) {
          throw new Error('Controlled parser bytes changed.');
        }
        process.stdout.write('preserved\n');
        """;

    [Test]
    [Arguments(1)]
    [Arguments(7)]
    [Arguments(4096)]
    public async Task AcIso007ControlledHeaderBoundaryPreservesBinaryBodyAcrossChunks(int blockSize)
    {
        using var directory = new ImageBundleTestDirectory();
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", Source, IsolatedAggregateNodeProcess.Module("isolated-github-headers.mjs"),
                directory.Root, blockSize.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output.Trim()).IsEqualTo("preserved");
        await Assert.That(result.Error).IsEmpty();
    }
}
