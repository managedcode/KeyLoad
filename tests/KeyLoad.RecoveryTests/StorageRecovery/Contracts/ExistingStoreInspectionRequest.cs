namespace KeyLoad.RecoveryTests;

internal sealed record ExistingStoreInspectionRequest(string Directory, Guid ExpectedNodeId,
    Guid? Incarnation, ExistingStoreInspectionVariant Variant)
{
    public override string ToString() => nameof(ExistingStoreInspectionRequest);
}
