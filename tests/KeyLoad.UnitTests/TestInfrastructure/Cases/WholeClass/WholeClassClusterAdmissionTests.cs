namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class WholeClassClusterAdmissionTests
{
    private const string FilterProperty = "filter";
    private const string RefusedProperty = "refused";
    private const string InputProperty = "inputPreserved";
    private const string RepairProperty = "exactRepair";
    private const string NativeProperty = "nativeFilterRetained";
    private const string UidProperty = "noUid";
    private const int Combined = 4;
    private const int CurrentCombined = 11;
    private const int SingleRefusals = 22;
    private const int CombinedRefusals = 23;
    private const int Properties = 6;

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    public async Task ExactWholeClassRefusesUnknownMixedAndInheritedIdentityThenOriginalSelectionIsUnchanged(int selection)
    {
        var filter = WholeClassClusterAdmissionFilters.For(selection);
        var result = await WholeClassClusterAdmissionProcess.ReadAsync(filter, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.EnumerateObject().Count()).IsEqualTo(Properties);
        await Assert.That(result.GetProperty(FilterProperty).GetString()).IsEqualTo(filter);
        await Assert.That(result.GetProperty(RefusedProperty).GetInt32()).IsEqualTo(selection is Combined or CurrentCombined ? CombinedRefusals : SingleRefusals);
        foreach (var property in new[] { InputProperty, RepairProperty, NativeProperty, UidProperty })
        {
            await Assert.That(result.GetProperty(property).GetBoolean()).IsTrue();
        }
    }
}
