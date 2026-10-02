using System.Text;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-CLIENT-009: actual persisted identities and unknown-length frames fit unchanged MCP admission pools.</summary>
internal sealed class McpUnknownLengthAdmissionTests
{
    private const string RootPrincipalId = "root";
    private const int DefaultHttpBodyLimitBytes = 8 * 1024 * 1024;
    private const int InitialFrameCapacityBytes = 16 * 1024;
    private const int UnknownLengthLimitBytes = 16_421;
    private const string DiscoveryFrame = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"server/discover\",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\"},\"clientInfo\":{\"name\":\"unit\",\"version\":\"1\"},\"clientCapabilities\":{}}}";

    /// <summary>A real persisted administrator and unknown-length discovery frame admit and fully release default pools.</summary>
    [Test]
    public async Task SmallUnknownLengthDiscoveryAdmitsAndReleasesDefaultMemoryAndExecutionLeases()
    {
        using var database = new TestDatabase();
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(RootPrincipalId)))!;
        var principalBytes = JsonDefaults.Serialize(principal);
        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(limits.DataBytes, limits.ControlBytes, limits.IngressBytes);
        var governor = new HttpAdmissionGovernor();
        var wire = Encoding.UTF8.GetBytes(DiscoveryFrame);

        using (var state = new McpRequestState(governor, memory, DefaultHttpBodyLimitBytes, CancellationToken.None))
        {
            state.Authenticate(principalBytes, CancellationToken.None);
            using var source = new MemoryStream(wire, writable: false);
            using var body = await McpFrameBody.ReadAsync(source, null, DefaultHttpBodyLimitBytes);
            await Assert.That(body.WireBytes).IsEqualTo(wire.Length);
            await Assert.That(body.RetainedCapacity).IsLessThanOrEqualTo(InitialFrameCapacityBytes);
            await Assert.That(body.RetainedCapacity).IsLessThan(DefaultHttpBodyLimitBytes);
            state.Attach(body);

            state.Admit(null, CancellationToken.None);

            await Assert.That(state.MaximumPayloadBytes).IsEqualTo(new HttpAdmissionLimits().MaxControlBodyBytes);
            await Assert.That(state.MaximumReplyBytes).IsEqualTo(McpFramingProtocol.MaximumControlReplyBytes);
        }

        var status = governor.Status();
        await Assert.That(status.Node.ControlCommands).IsEqualTo(0);
        await Assert.That(status.VerifiedScopes.ControlCommands).IsEqualTo(0);
        AssertFullMemoryPoolsCanBeReserved(memory, limits);
        AssertFullMemoryPoolsCanBeReserved(memory, limits);
    }

    /// <summary>A small unknown-length frame retains only the bounded initial buffer and replays exact bytes.</summary>
    [Test]
    public async Task UnknownLengthSmallBodyRetainsScratchCapacityAndExactReplay()
    {
        var wire = Encoding.UTF8.GetBytes("{\"method\":\"server/discover\"}");
        using var source = new TemporaryFrameFile(wire);
        using var input = source.OpenRead();
        using var body = await McpFrameBody.ReadAsync(input, null, DefaultHttpBodyLimitBytes);

        await Assert.That(body.WireBytes).IsEqualTo(wire.Length);
        await Assert.That(body.RetainedCapacity).IsLessThanOrEqualTo(InitialFrameCapacityBytes);
        await Assert.That(body.RetainedCapacity).IsLessThan(DefaultHttpBodyLimitBytes);
        await Assert.That(body.Bytes.Span.SequenceEqual(wire)).IsTrue();
        var replay = new byte[wire.Length];
        using var replayReader = body.OpenReader();
        await replayReader.ReadExactlyAsync(replay.AsMemory());
        await Assert.That(replay.AsSpan().SequenceEqual(wire)).IsTrue();
    }

    /// <summary>Unknown-length growth accepts the inclusive non-power-of-two maximum without exceeding it.</summary>
    [Test]
    public async Task UnknownLengthBodyGrowsToExactNonPowerOfTwoMaximum()
    {
        var json = $"{{\"x\":\"{new string('a', UnknownLengthLimitBytes - 8)}\"}}";
        var wire = Encoding.UTF8.GetBytes(json);
        await Assert.That(wire.Length).IsEqualTo(UnknownLengthLimitBytes);
        using var source = new TemporaryFrameFile(wire);
        using var input = source.OpenRead();
        using var body = await McpFrameBody.ReadAsync(input, null, UnknownLengthLimitBytes);

        await Assert.That(body.WireBytes).IsEqualTo(UnknownLengthLimitBytes);
        await Assert.That(body.RetainedCapacity).IsGreaterThan(InitialFrameCapacityBytes);
        await Assert.That(body.RetainedCapacity).IsLessThanOrEqualTo(UnknownLengthLimitBytes);
        await Assert.That(body.Bytes.Span.SequenceEqual(wire)).IsTrue();
    }

    private static void AssertFullMemoryPoolsCanBeReserved(McpMemoryBudget memory, McpMemoryLimits limits)
    {
        using var data = memory.Reserve(McpMemoryLane.Data, limits.DataBytes);
        using var control = memory.Reserve(McpMemoryLane.Control, limits.ControlBytes);
        using var ingress = memory.Reserve(McpMemoryLane.Ingress, limits.IngressBytes);
    }
}
