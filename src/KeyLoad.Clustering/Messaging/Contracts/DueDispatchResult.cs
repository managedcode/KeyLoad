namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(DueCoordinatorAliases.Result)]
internal sealed record DueDispatchResult([property: global::Orleans.Id(DueCoordinatorFields.Error)] ErrorCode? Error);
