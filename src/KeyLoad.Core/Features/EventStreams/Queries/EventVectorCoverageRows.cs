using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorCoverageRows
{
    private const string Missing = "The authorized native event vector coverage row is unavailable.";

    internal static EventVectorCoverageRow Required(IKeyValueView boundedView, byte[] key,
        EventVectorInventoryReadBudget budget) => Optional(boundedView, key, budget)
        ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Missing);

    internal static EventVectorCoverageRow? Optional(IKeyValueView boundedView, byte[] key,
        EventVectorInventoryReadBudget budget)
    {
        ArgumentNullException.ThrowIfNull(boundedView);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(budget);
        EventVectorCoverageRow? row = null;
        boundedView.ReadValue(key, bytes => row = new EventVectorCoverageRow
        { Key = key.ToArray(), Value = bytes.ToArray() }, budget.Observe);
        return row;
    }
}
