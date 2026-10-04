namespace KeyLoad.Server;

/// <summary>Owns only checked optional routing headers, never caller authority or session state.</summary>
/// <param name="Method">The checked HTTP method hint, absent for native missing-header validation.</param>
/// <param name="Name">The checked target hint, absent for native missing-header validation.</param>
internal readonly record struct McpTransportHeaders(string? Method, string? Name);
