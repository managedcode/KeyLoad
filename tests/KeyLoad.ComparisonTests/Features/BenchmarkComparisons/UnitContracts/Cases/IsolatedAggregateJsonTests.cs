using System.Text.Json;
using F = KeyLoad.UnitTests.Features.BenchmarkComparisons.IsolatedAggregateFields;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-007 raw parsing rejects ambiguous or malformed input before publication.</summary>
internal sealed class IsolatedAggregateJsonTests
{
    private const string ModuleMode = "--input-type=module";
    private const string EvaluationMode = "-e";
    private const string Module = "aggregate-json.mjs";
    private const string Probe = """
        import {pathToFileURL} from 'node:url';
        const module=await import(pathToFileURL(process.argv[1]).href);
        const inputs=['{"name":1,"na\\u006de":2}', '{"nested":{"value":1,"value":2}}',
          '{"name":1} trailing', '{"array":[1,2],"nested":{"name":"value"}}'];
        const accepted=inputs.map(value=>{try{module.parseBytes(Buffer.from(value));return true;}catch{return false;}});
        process.stdout.write(JSON.stringify({accepted}));
        """;

    [Test]
    public async Task AC_ISO_007_RejectsDuplicateEscapedNestedKeysAndMalformedRawJson()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var response = await IsolatedAggregateNodeProcess.RunAsync(
            [ModuleMode, EvaluationMode, Probe, IsolatedAggregateNodeProcess.Module(Module)], token);
        await Assert.That(response.ExitCode).IsEqualTo(0);
        await Assert.That(response.Error).IsEqualTo(string.Empty);
        using var result = JsonDocument.Parse(response.Output);
        var accepted = result.RootElement.GetProperty(F.Accepted).EnumerateArray().Select(value => value.GetBoolean()).ToArray();
        await Assert.That(accepted.Length).IsEqualTo(4);
        await Assert.That(accepted[0]).IsFalse();
        await Assert.That(accepted[1]).IsFalse();
        await Assert.That(accepted[2]).IsFalse();
        await Assert.That(accepted[3]).IsTrue();
    }
}
