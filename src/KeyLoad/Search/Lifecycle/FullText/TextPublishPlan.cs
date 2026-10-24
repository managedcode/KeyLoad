namespace KeyLoad.Server.Search;

internal sealed record TextPublishPlan(TextGenerationSlot? Previous, bool ReplaceCurrent);
