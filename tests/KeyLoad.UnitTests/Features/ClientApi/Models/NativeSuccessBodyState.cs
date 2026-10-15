using KeyLoad.Client;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeSuccessBodyState(ConnectionNativeScenario scenario, PrincipalRecord principal,
    NativeSuccessBodyEndpoint endpoint, KeyLoadClient client, DeclaredResponseEofTasks tasks,
    AtomicPartitionPlacementResolution placement)
{
    internal ConnectionNativeScenario Scenario
    {
        get;
    } = scenario;
    internal PrincipalRecord Principal
    {
        get;
    } = principal;
    internal NativeSuccessBodyEndpoint Endpoint
    {
        get;
    } = endpoint;
    internal KeyLoadClient Client
    {
        get;
    } = client;
    internal DeclaredResponseEofTasks Tasks
    {
        get;
    } = tasks;
    internal AtomicPartitionPlacementResolution Placement
    {
        get;
    } = placement;
    internal CommitReceipt? FirstReceipt
    {
        get;
        set;
    }
    internal DeclaredResponseEofCut? FirstCut
    {
        get;
        set;
    }
}
