namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalOwnershipProtocol
{
    internal const string FirstStatement = "portal-owner-first";
    internal const string SecondStatement = "portal-owner-second";
    internal const string OldPortal = "portal-owner-old";
    internal const string Survivor = "portal-owner-survivor";
    internal const string LeftAlias = "l";
    internal const string RightAlias = "r";
    internal const string PortalPrefix = "portal-owner-slot-";
    internal const string OverflowPortal = "portal-owner-overflow";
    internal const int Slots = 8;
    internal const int ColdCuts = 2;
    internal const int JsonbOid = 3802;
    internal const short UnknownLength = -1;
    internal const byte Parse = (byte)'P';
    internal const byte Bind = (byte)'B';
    internal const byte Describe = (byte)'D';
    internal const byte Execute = (byte)'E';
    internal const byte Close = (byte)'C';
    internal const byte Flush = (byte)'H';
    internal const byte Sync = (byte)'S';
    internal const byte QueryMessage = (byte)'Q';
    internal const byte Terminate = (byte)'X';
    internal const byte ParseComplete = (byte)'1';
    internal const byte BindComplete = (byte)'2';
    internal const byte CloseComplete = (byte)'3';
    internal const byte RowDescription = (byte)'T';
    internal const byte DataRow = (byte)'D';
    internal const byte CommandComplete = (byte)'C';
    internal const byte Error = (byte)'E';
    internal const byte Ready = (byte)'Z';
    internal const byte Idle = (byte)'I';
    internal const byte Statement = (byte)'S';
    internal const byte Portal = (byte)'P';
    internal const string Validation = "SERROR\0C22023\0MValidation\0\0";
    internal const string PermissionDenied = "SERROR\0C42501\0MPermissionDenied\0\0";
    internal const string Exhausted = "SERROR\0C54000\0MResourceExhausted\0\0";
    internal const string Command = "SELECT 1\0";
    internal const string Terminator = "\0";
    internal static string Slot(int index)
        => PortalPrefix + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
}
