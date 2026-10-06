using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopIsolatedPlanTests
{
    [Test]
    public async Task AcScale018CreatesExactIndependentCanonicalPlanThroughNode()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var executionOptions = OpenLoopPlanProcessOptionsBinding.Capture();
        await OpenLoopPlanTestLifetime.RunAsync(async (outputPath, token) =>
        {
            await Assert.That(File.Exists(outputPath)).IsFalse();
            var scaled = await OpenLoopPlanNodeProcess.RunAsync(executionOptions, OpenLoopPlanNodeProgram.ScaledOperation,
                input: null, outputPath: null, keepStandardInputOpen: false, ready: null, cancellationToken: token);
            await Assert.That(scaled.ExitCode).IsEqualTo(0);
            using var scaledDocument = JsonDocument.Parse(scaled.Output);
            using var contractDocument = await OpenLoopPlanNodeProcess.ReadContractAsync(token);
            var created = await OpenLoopPlanNodeProcess.RunAsync(executionOptions, OpenLoopPlanNodeProgram.CreateOperation,
                input: null, outputPath: outputPath, keepStandardInputOpen: false, ready: null, cancellationToken: token);
            await Assert.That(created.ExitCode).IsEqualTo(0);
            await Assert.That(created.Error).IsEqualTo(string.Empty);
            await using var planStream = File.OpenRead(outputPath);
            using var planDocument = await JsonDocument.ParseAsync(planStream, cancellationToken: token);
            await OpenLoopPlanAssertions.VerifyCanonicalAsync(planDocument.RootElement, contractDocument.RootElement,
                scaledDocument.RootElement);
            var originalBytes = await File.ReadAllBytesAsync(outputPath, token);
            var duplicate = await OpenLoopPlanNodeProcess.RunAsync(executionOptions, OpenLoopPlanNodeProgram.CreateOperation,
                input: null, outputPath: outputPath, keepStandardInputOpen: false, ready: null, cancellationToken: token);
            await Assert.That(duplicate.ExitCode).IsNotEqualTo(0);
            await Assert.That(await File.ReadAllBytesAsync(outputPath, token)).IsEquivalentTo(originalBytes,
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }, cancellationToken);
    }

    [Test]
    public async Task AcScale018RejectsChangedPlanWithoutReplacingCreateOnlyOutput()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var executionOptions = OpenLoopPlanProcessOptionsBinding.Capture();
        await OpenLoopPlanTestLifetime.RunAsync(async (outputPath, token) =>
        {
            var scaled = await OpenLoopPlanNodeProcess.RunAsync(executionOptions, OpenLoopPlanNodeProgram.ScaledOperation,
                input: null, outputPath: null, keepStandardInputOpen: false, ready: null, cancellationToken: token);
            using var scaledDocument = JsonDocument.Parse(scaled.Output);
            using var contractDocument = await OpenLoopPlanNodeProcess.ReadContractAsync(token);
            var created = await OpenLoopPlanNodeProcess.RunAsync(executionOptions, OpenLoopPlanNodeProgram.CreateOperation,
                input: null, outputPath: outputPath, keepStandardInputOpen: false, ready: null, cancellationToken: token);
            await Assert.That(created.ExitCode).IsEqualTo(0);
            var before = await File.ReadAllBytesAsync(outputPath, token);
            var invalid = await File.ReadAllTextAsync(outputPath, token);
            var candidate = JsonNode.Parse(invalid)!.AsObject();
            candidate["measurementCells"]![0]!["offeredRatePerSecond"] = OpenLoopPlanExpectedInventory.InvalidOfferedRate;
            await VerifyRejectedAsync(candidate, outputPath, before, executionOptions, token);
            candidate = JsonNode.Parse(invalid)!.AsObject();
            var cells = candidate["measurementCells"]!.AsArray();
            var first = cells[0]!.DeepClone();
            cells[0] = cells[1]!.DeepClone();
            cells[1] = first;
            await VerifyRejectedAsync(candidate, outputPath, before, executionOptions, token);
            candidate = JsonNode.Parse(invalid)!.AsObject();
            var firstCell = candidate["measurementCells"]![0]!.AsObject();
            candidate["measurementCells"]![0] = ReverseCellProperties(firstCell);
            await VerifyRejectedAsync(candidate, outputPath, before, executionOptions, token);
            using var verified = JsonDocument.Parse(invalid);
            await OpenLoopPlanAssertions.VerifyCanonicalAsync(verified.RootElement, contractDocument.RootElement,
                scaledDocument.RootElement);
        }, cancellationToken);
    }

    [Test]
    public async Task AcScale018CancellationJoinsActualPlannerBeforeHealthyPublication()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var executionOptions = OpenLoopPlanProcessOptionsBinding.Capture();
        await OpenLoopPlanTestLifetime.RunAsync(async (outputPath, token) =>
        {
            await OpenLoopPlanCancellationFlow.CancelOwnedPlannerAsync(executionOptions, outputPath, token);
            await Assert.That(File.Exists(outputPath)).IsFalse();
            var recovered = await OpenLoopPlanNodeProcess.RunAsync(executionOptions,
                OpenLoopPlanNodeProgram.CreateOperation, input: null, outputPath: outputPath,
                keepStandardInputOpen: false, ready: null, cancellationToken: token);
            await Assert.That(recovered.ExitCode).IsEqualTo(0);
            await Assert.That(recovered.Error).IsEqualTo(string.Empty);
            using var contractDocument = await OpenLoopPlanNodeProcess.ReadContractAsync(token);
            var scaled = await OpenLoopPlanNodeProcess.RunAsync(executionOptions,
                OpenLoopPlanNodeProgram.ScaledOperation, input: null, outputPath: null,
                keepStandardInputOpen: false, ready: null, cancellationToken: token);
            using var scaledDocument = JsonDocument.Parse(scaled.Output);
            await using var planStream = File.OpenRead(outputPath);
            using var planDocument = await JsonDocument.ParseAsync(planStream, cancellationToken: token);
            await OpenLoopPlanAssertions.VerifyCanonicalAsync(planDocument.RootElement,
                contractDocument.RootElement, scaledDocument.RootElement);
        }, cancellationToken);
    }

    private static async Task VerifyRejectedAsync(JsonObject candidate, string outputPath, byte[] originalBytes,
        IOptions<OpenLoopPlanProcessOptions> executionOptions, CancellationToken cancellationToken)
    {
        var validation = await OpenLoopPlanNodeProcess.RunAsync(executionOptions,
            OpenLoopPlanNodeProgram.ValidateOperation, input: candidate.ToJsonString(), outputPath: outputPath,
            keepStandardInputOpen: false, ready: null, cancellationToken: cancellationToken);
        await Assert.That(validation.ExitCode).IsEqualTo(0);
        using var result = JsonDocument.Parse(validation.Output);
        await Assert.That(result.RootElement.GetProperty("rejected").GetBoolean()).IsTrue();
        await Assert.That(await File.ReadAllBytesAsync(outputPath, cancellationToken)).IsEquivalentTo(originalBytes,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static JsonObject ReverseCellProperties(JsonObject source)
    {
        var reversed = new JsonObject();
        foreach (var field in OpenLoopPlanExpectedInventory.CellFields.Reverse())
        {
            reversed.Add(field, source[field]?.DeepClone());
        }
        return reversed;
    }
}
