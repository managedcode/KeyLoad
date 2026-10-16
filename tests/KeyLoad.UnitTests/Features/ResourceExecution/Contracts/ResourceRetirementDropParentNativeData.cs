namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementDropParentNativeData
{
    internal const string Root = "root";
    internal const string Table = "native-parent-table";
    internal const string Id = "id";
    internal const string Value = "value";
    internal const string Row = "row";
    internal const string PutKind = "putDocument";
    internal const string InitialJson = "{\"id\":\"row\",\"value\":\"initial\"}";
    internal const string HealthyJson = "{\"id\":\"row\",\"value\":\"healthy\"}";
    internal const string IdentitySpace = "resource-retirement-drop-parent-id-v1";
    internal const string StateKey = "KeyLoad.DropParent.NativeState";
    internal const int Version = 1;
    internal const int First = 1;
    internal const int Zero = 0;
    internal const int HealthyRevision = 2;
    internal const int ImageLimit = 4_096;
    internal const int ColdWaves = 2;
    internal const int PointerIndex = 1;
}
