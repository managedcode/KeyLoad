using KeyLoad.Client;
using KeyLoad.UnitTests.OrleansExecution;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed class SuccessBodyState(ConnectionScenario scenario, PrincipalRecord principal,
    SuccessBodyEndpoint endpoint, KeyLoadClient client, DeclaredResponseEofTasks tasks,
    AtomicPartitionPlacementResolution placement)
{
    internal ConnectionScenario Scenario
    {
        get;
    } = scenario;
    internal PrincipalRecord Principal
    {
        get;
    } = principal;
    internal SuccessBodyEndpoint Endpoint
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
