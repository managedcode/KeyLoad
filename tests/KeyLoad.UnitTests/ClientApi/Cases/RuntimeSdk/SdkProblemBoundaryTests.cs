namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class SdkProblemBoundaryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task PermissionProblemPreservesExactInclusiveCapAndRejectsOneUnderThenHealthyConnection(bool declared)
        => SdkProblemBoundaryFlow.RunAsync(declared);
}
