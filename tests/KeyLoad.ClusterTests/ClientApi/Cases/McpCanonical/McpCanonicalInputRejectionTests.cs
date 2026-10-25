using KeyLoad.ClusterTests.DocumentStorage;

namespace KeyLoad.ClusterTests.ClientApi;

/// <summary>AC-MCP-003/007: execution public calls reject invalid canonical requests before starting an operation grain.</summary>
/// <param name="fixture">The initialized actual execution Aspire Orleans application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpCanonicalInputRejectionTests(ClusterFixture fixture)
{
    /// <summary>Actual null and missing-required-body-field arguments fail safely and leave a later valid call usable.</summary>
    /// <param name="nullBody">Selects null instead of an actual object missing its required reference.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcMcp003InvalidCanonicalBodyHasNoExecutionIdentity(bool nullBody)
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpCallerProtocol.Request] = nullBody ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
        };
        var rejected = await session.Client.InvokeKeyLoadToolAsync(McpCallerTools.DocumentsGet, arguments, cancellationToken: deadline.Token);
        _ = (await McpCallerAssertions.ErrorAsync(rejected, ErrorCode.Validation, dispatched: false)).Value;
        var valid = await PublicReadAssertions.DocumentAsync(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference), deadline.Token));
        await Assert.That(valid.Value).IsNull();
    }
}
