using KeyLoad.Server;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCPGW-004: local invokers preserve descriptor identity and fail closed without request admission.</summary>
internal sealed class McpGatewayCatalogAdapterTests
{
    private const string DocumentGet = "keyload_documents_get";

    [Test]
    public async Task NativeFunctionExposesExactCanonicalNameDescriptionAndSchemas()
    {
        var operation = McpOperationCatalog.Entries.Single(item => item.Name == DocumentGet);
        var function = new McpGatewayCanonicalToolFunction(operation, new HttpContextAccessor());

        await Assert.That(function.Name).IsEqualTo(operation.Name);
        await Assert.That(function.Description).IsEqualTo(operation.Description);
        await Assert.That(function.JsonSchema.GetRawText()).IsEqualTo(operation.InputSchema.GetRawText());
        await Assert.That(function.ReturnJsonSchema!.Value.GetRawText()).IsEqualTo(operation.OutputSchema.GetRawText());
    }

    [Test]
    public async Task GatewayInvocationCannotDispatchWithoutCurrentAdmittedRequest()
    {
        await using var host = McpGatewayCatalogTestHost.Create();
        await host.Owner.InitializeAsync(CancellationToken.None);

        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => host.Owner.InvokeAsync(
            DocumentGet,
            new Dictionary<string, object?>(),
            CancellationToken.None));
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
    }
}
