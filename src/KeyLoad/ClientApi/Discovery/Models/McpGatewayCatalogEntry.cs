using ManagedCode.MCPGateway;

namespace KeyLoad.Server;

/// <summary>One canonical operation and the bounded hints used only by disposable graph search.</summary>
internal sealed record McpGatewayCatalogEntry(
    McpOperationDescriptor Operation,
    McpGatewayToolSearchHints SearchHints);
