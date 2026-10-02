namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>The actual canonical decoded value and the server's independent execution identity.</summary>
/// <typeparam name="T">The actual canonical result type.</typeparam>
/// <param name="Value">The native structured result decoded with canonical options.</param>
/// <param name="RequestId">The actual operation GUID returned by the server.</param>
internal readonly record struct McpCallReceipt<T>(T Value, Guid RequestId);
