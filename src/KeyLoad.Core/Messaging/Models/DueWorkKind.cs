namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(DueWorkAliases.Kind)]
internal enum DueWorkKind
{
    Schedule = 0,
    Saga = 1
}
