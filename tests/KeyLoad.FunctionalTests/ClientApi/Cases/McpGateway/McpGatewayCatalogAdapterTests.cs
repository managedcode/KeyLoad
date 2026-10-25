using KeyLoad.Server;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>AC-MCPGW-004: local invokers preserve descriptor identity, fail closed without request admission and refuse
/// arguments outside the canonical input schema as Validation.</summary>
internal sealed class McpGatewayCatalogAdapterTests
{
    private const string DocumentGet = "keyload_documents_get";

    [Test]
    public async Task FunctionExposesExactCanonicalNameDescriptionAndSchemas()
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

        // Schema-valid arguments reach the tool, which refuses because no admitted request owns the call.
        var admittedShape = new Dictionary<string, System.Text.Json.JsonElement>
        {
            [McpCanonicalTestData.RequestKey] = System.Text.Json.JsonSerializer.SerializeToElement(
                new GetDocumentRequest(McpCanonicalTestData.Reference), JsonDefaults.Options)
        };
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => host.Owner.InvokeAsync(
            DocumentGet, admittedShape, CancellationToken.None));
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.RecoveryRequired);

        var malformed = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => host.Owner.InvokeAsync(
            DocumentGet, new Dictionary<string, System.Text.Json.JsonElement>(), CancellationToken.None));
        await Assert.That(malformed!.Code).IsEqualTo(ErrorCode.Validation);
    }
}
