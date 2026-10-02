namespace KeyLoad.Server;

/// <summary>Immutable request dimensions used to project retained MCP memory before decoding and execution.</summary>
/// <param name="Capacity">The full private request-body buffer capacity retained by the HTTP owner.</param>
/// <param name="WireBytes">The actual validated request-body bytes.</param>
/// <param name="Shape">The bounded token and property counts from framing inspection.</param>
/// <param name="MaximumPayloadBytes">The fixed maximum canonical operation-payload buffer capacity.</param>
/// <param name="AuthenticationBytes">The actual retained authentication reply bytes.</param>
/// <param name="AuthenticationShape">The bounded token and property counts of the authentication reply.</param>
internal readonly record struct McpInputMemory(
    int Capacity,
    int WireBytes,
    McpFrameShape Shape,
    int MaximumPayloadBytes,
    int AuthenticationBytes,
    McpFrameShape AuthenticationShape);
