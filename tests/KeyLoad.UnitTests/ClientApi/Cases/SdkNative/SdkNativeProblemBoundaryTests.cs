namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class SdkNativeProblemBoundaryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task NativePermissionProblemPreservesExactInclusiveCapAndRejectsOneUnderThenHealthyConnection(bool declared)
        => SdkNativeProblemBoundaryFlow.RunAsync(declared);
}
