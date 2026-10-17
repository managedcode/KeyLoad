namespace KeyLoad.Server;

internal static class PostgresMessageProtocol
{
    internal const byte SimpleQuery = (byte)'Q';
    internal const byte Parse = (byte)'P';
    internal const byte Bind = (byte)'B';
    internal const byte Describe = (byte)'D';
    internal const byte Execute = (byte)'E';
    internal const byte Close = (byte)'C';
    internal const byte Sync = (byte)'S';
    internal const byte Flush = (byte)'H';
    internal const byte Terminate = (byte)'X';
    internal const byte Password = (byte)'p';
    internal const byte Statement = (byte)'S';
    internal const byte Portal = (byte)'P';
    internal const int PasswordChallenge = 3;
    internal const int AuthenticationOk = 0;
    internal const string ServerVersion = "server_version";
    internal const string ServerVersionValue = "18.0 (KeyLoad native-sql-v1)";
    internal const string Profile = "keyload_protocol_profile";
    internal const string ProfileValue = "native-sql-v1-jsonb-result";
    internal const string Encoding = "server_encoding";
    internal const string StandardStrings = "standard_conforming_strings";
    internal const string StandardStringsOn = "on";
}
