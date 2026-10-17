namespace KeyLoad.Server.Features.ResourceExecution;

[global::Orleans.Alias(NativeResourceRetirementDiagnosticProtocol.DiagnosticOperationAlias)]
internal enum ResourceRetirementDiagnosticOperation
{
    Execute = 0,
    ReadState = 1,
    ReadPreparation = 2
}
