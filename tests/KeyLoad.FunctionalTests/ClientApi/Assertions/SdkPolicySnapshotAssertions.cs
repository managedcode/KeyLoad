namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SdkPolicySnapshotAssertions
{
    private const string ProblemTypePrefix = "urn:keyload:error:";

    internal static async Task ProblemAsync<T>(global::ManagedCode.Communication.Result<T> actual,
        string expectedCode, int expectedStatus, string expectedDetail)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Problem).IsNotNull();
        await Assert.That(actual.Problem!.Type).IsEqualTo(ProblemTypePrefix + expectedCode);
        await Assert.That(actual.Problem.Title).IsEqualTo(expectedCode);
        await Assert.That(actual.Problem.ErrorCode).IsEqualTo(expectedCode);
        await Assert.That(actual.Problem.StatusCode).IsEqualTo(expectedStatus);
        await Assert.That(actual.Problem.Detail).IsEqualTo(expectedDetail);
    }

    internal static async Task HealthyAsync(NodeStatus actual, NodeStatus expected)
    {
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.SiloCount).IsEqualTo(expected.SiloCount);
        await Assert.That(actual.ReplicaCount).IsEqualTo(expected.ReplicaCount);
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
        await Assert.That(actual.RoutingReady).IsEqualTo(expected.RoutingReady);
        await Assert.That(actual.ProcessId).IsEqualTo(expected.ProcessId);
        await Assert.That(actual.ReadGeneration).IsEqualTo(expected.ReadGeneration);
    }
}
