namespace KeyLoad.Server;

internal static class ServerConnectionProtocol
{
    internal const int NoFailures = 0;
    internal const int NoOperations = 0;
    internal const string Unavailable = "The request has no server-owned transport connection.";
    internal const string Closed = "The server-owned transport connection is closing.";
    internal const string Idle = "The server-owned transport connection has expired while idle.";
    internal const string Capacity = "The connection operation limit has been reached.";
}
