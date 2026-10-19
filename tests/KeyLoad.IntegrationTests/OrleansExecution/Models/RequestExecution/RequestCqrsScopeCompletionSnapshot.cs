namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal readonly record struct RequestCqrsScopeCompletionSnapshot(
    RequestCqrsCompletionCallSnapshot ExplicitNode1,
    RequestCqrsCompletionCallSnapshot ExplicitNode2,
    RequestCqrsCompletionCallSnapshot ExplicitNode3,
    RequestCqrsCompletionCallSnapshot BatchNode1,
    RequestCqrsCompletionCallSnapshot BatchNode2,
    RequestCqrsCompletionCallSnapshot BatchNode3);
