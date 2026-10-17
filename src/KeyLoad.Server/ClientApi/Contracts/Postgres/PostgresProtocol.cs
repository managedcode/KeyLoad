namespace KeyLoad.Server;

internal static class PostgresProtocol
{
    internal const int Version = 196608;
    internal const int SslRequest = 80877103;
    internal const int CancelRequest = 80877102;
    internal const int Int32Oid = 23;
    internal const int Int64Oid = 20;
    internal const int Int16Oid = 21;
    internal const int BooleanOid = 16;
    internal const int TextOid = 25;
    internal const int JsonOid = 114;
    internal const int JsonbOid = 3802;
    internal const int InferOid = 0;
    internal const short TextFormat = 0;
    internal const short BinaryFormat = 1;
    internal const int NullLength = -1;
    internal const byte JsonbVersion = 1;
    internal const int AllRows = 0;
    internal const int FirstField = 0;
    internal const int EmptyBytes = 0;
    internal const int NoTableOid = 0;
    internal const short NoTableAttribute = 0;
    internal const byte StartupMessage = 0;
    internal const byte TextTerminator = 0;
    internal const byte IdleStatus = (byte)'I';
    internal const byte SslAccepted = (byte)'S';
    internal const string Terminator = "\0";
    internal const string CommandSeparator = " ";
    internal const int MaximumParameters = 64;
    internal const char ParameterPrefix = 'p';
    internal const string Invalid = "The native SQL message is invalid.";
    internal const string Unsupported = "The native SQL capability is unsupported.";
    internal const string Duplicate = "The named native SQL statement or portal already exists.";
    internal const string InterruptedReply = "The native SQL write receipt could not be delivered; resolve its original command identity.";
}
