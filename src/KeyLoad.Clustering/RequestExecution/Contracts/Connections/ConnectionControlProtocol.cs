namespace KeyLoad.Clustering;

internal static class ConnectionControlProtocol
{
    internal const string Alias = "keyload.connection.control";
    internal const string Purpose = "keyload:connection-close";
    internal const int MaximumTokenCharacters = 8_192;
    internal const string Closed = "The server-owned connection is closing.";
    internal const string Capacity = "The connection operation limit has been reached.";
}
