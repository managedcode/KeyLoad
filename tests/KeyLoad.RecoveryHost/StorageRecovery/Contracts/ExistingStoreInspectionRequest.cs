namespace KeyLoad.RecoveryHost;

internal sealed record ExistingStoreInspectionRequest(string Directory, Guid ExpectedNodeId,
    Guid? Incarnation, ExistingStoreInspectionVariant Variant)
{
    public override string ToString() => nameof(ExistingStoreInspectionRequest);
}
