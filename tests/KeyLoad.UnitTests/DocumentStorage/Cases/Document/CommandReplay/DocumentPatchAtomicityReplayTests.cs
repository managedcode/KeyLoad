namespace KeyLoad.UnitTests.DocumentStorage;

internal sealed class DocumentPatchAtomicityReplayTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public Task AcDstoreLatePatchRollsBackThenRetainedReplaySurvivesRepairAndTwoColdOwners(int fault)
        => DocumentPatchAtomicityOwner.RunAsync(owner => DocumentPatchAtomicityTrial.RunAsync(owner, fault));
}
