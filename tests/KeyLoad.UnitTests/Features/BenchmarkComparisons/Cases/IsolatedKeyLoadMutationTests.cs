using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKeyLoadMutationTests
{
    [Test]
    public async Task AcIso005KeyLoadMutationsRequireFreshCreateAndExistingRevisionForUpdateDelete()
    {
        var input = new BenchmarkDataset(ComparisonHarnessInputs.Small).Documents[0];
        var create = (PutDocument)KeyLoadDocumentOperations.CreateMutation(Scenario.DocumentWrite, input);
        var update = (PutDocument)KeyLoadDocumentOperations.CreateMutation(Scenario.DocumentUpdate, input);
        var delete = (DeleteDocument)KeyLoadDocumentOperations.CreateMutation(Scenario.DocumentDelete, input);
        await Assert.That(create.ExpectedRevision).IsEqualTo(0);
        await Assert.That(update.ExpectedRevision).IsEqualTo(1);
        await Assert.That(update.ExplicitReplacement).IsTrue();
        await Assert.That(delete.ExpectedRevision).IsEqualTo(1);
        await Assert.That(update.Json).IsEqualTo(input.Json);
        await Assert.That(delete.Id).IsEqualTo(input.Id);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyLoadDocumentOperations.CreateMutation(Scenario.VectorExact, input));
    }
}
