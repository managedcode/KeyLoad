namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class TestDatabaseDeleteFailureProtocol
{
    internal const int NoFailures = 0;
    internal const int InventoryLookahead = 1;
    internal const string SearchPattern = "*";
    internal const string UnixRequired = "The genuine fixture deletion flow requires Unix mode enforcement.";
    internal const string EnumerationMustRefuse = "The original root permission precondition did not refuse enumeration; deletion was not attempted.";
    internal const string DeleteMustRefuse = "The original fixture deletion did not refuse the denied root.";
    internal const string InventoryExceeded = "The complete execution fixture inventory exceeded its configured observation budget.";
    internal const string EntryRequired = "The original execution fixture inventory cannot contain a link.";
    internal const UnixFileMode ReadWriteBits = UnixFileMode.UserRead | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
}
