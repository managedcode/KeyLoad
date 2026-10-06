using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCPGW-005: hard bounds reject before gateway graph search can run.</summary>
internal sealed class McpGatewayCatalogBoundsTests
{
    private const string ValidQuery = "documents";

    [Test]
    public async Task StaticCatalogFitsOperationAndMetadataCaps()
    {
        var options = UnitMcpOptions.Execution();
        var entries = McpGatewayCatalogValidation.CreateEntries(McpOperationCatalog.Entries, options);
        await Assert.That(entries.Length).IsEqualTo(McpOperationCatalog.Entries.Length);
        await Assert.That(entries.Length).IsLessThanOrEqualTo(options.Value.MaximumCatalogOperations);
        await Assert.That(entries.All(item => item.Operation.Description.Length <=
            options.Value.MaximumDescriptorCharacters)).IsTrue();
    }

    [Test]
    public async Task NativeSearchRejectsOverLimitBeforeInitialization()
    {
        await using var host = McpGatewayCatalogTestHost.Create();
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            host.Owner.SearchAsync(ValidQuery, host.ExecutionOptions.Value.MaximumSearchResults + 1, CancellationToken.None));
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task NativeRouteRejectsInvalidBoundsAndUtf8QueryBeforeInitialization()
    {
        await using var host = McpGatewayCatalogTestHost.Create();
        var categoryFailure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            host.Owner.RouteAsync(ValidQuery, host.ExecutionOptions.Value.MaximumRouteResults + 1, 1, null, CancellationToken.None));
        var utf8Failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            host.Owner.RouteAsync(new string('\u4e00', 683), 1, 1, null, CancellationToken.None));
        await Assert.That(categoryFailure).IsNotNull();
        await Assert.That(utf8Failure).IsNotNull();
        await Assert.That(categoryFailure!.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(utf8Failure!.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task QueryByteBoundaryIsInclusiveAndMalformedUtf16IsRejected()
    {
        await using var host = McpGatewayCatalogTestHost.Create();
        McpGatewayCatalogRequestValidation.ValidateQuery(new string('a',
            host.ExecutionOptions.Value.MaximumDiscoveryQueryBytes), host.ExecutionOptions);
        var tooLong = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            host.Owner.SearchAsync(new string('a', host.ExecutionOptions.Value.MaximumDiscoveryQueryBytes + 1),
                1, CancellationToken.None));
        var invalidUtf16 = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            host.Owner.SearchAsync("\ud800", 1, CancellationToken.None));
        await Assert.That(tooLong).IsNotNull();
        await Assert.That(invalidUtf16).IsNotNull();
        await Assert.That(tooLong!.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(invalidUtf16!.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task CancelledGraphInitializationFailsClosedInsteadOfPublishingAnEmptyIndex()
    {
        await using var host = McpGatewayCatalogTestHost.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => host.Owner.InitializeAsync(cancellation.Token));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            host.Owner.SearchAsync(ValidQuery, 1, CancellationToken.None));
    }
}
