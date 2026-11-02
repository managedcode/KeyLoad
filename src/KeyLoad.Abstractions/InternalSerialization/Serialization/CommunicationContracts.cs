using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Surrogates;

namespace KeyLoad.InternalSerialization;

// Inspect the selected library's actual generated surrogates; do not mirror their wire contracts.
internal static class CommunicationContracts
{
    internal static bool TryProject(object /* KLD0041: The validation graph selects the actual ManagedCode converter from this runtime node type. */ value, out object? /* KLD0041: The selected producer-owned surrogate has a runtime-specific struct shape inspected by the Id-field reflection walker. */ surrogate)
    {
        surrogate = value switch
        {
            Command<DatabaseCommand> command /* KLD0042: The runtime reflection node selects this exact producer-owned surrogate before validation of its actual Id fields. */ => new CommandTSurrogateConverter<DatabaseCommand>().ConvertToSurrogate(command) /* KLD0042: The actual producer CommandTSurrogate struct is boxed only for existing reflection validation of its Id fields. */,
            Result<object> result /* KLD0042: The runtime reflection node selects this exact producer-owned surrogate before validation of its actual Id fields. */ => ProjectResult(result) /* KLD0041: This branch validates the current stored Result<object> surrogate schema selected by the producer converter. */ /* KLD0042: The producer result surrogate is boxed only to expose its actual Id fields to the reflection walker. */,
            Problem problem /* KLD0042: The runtime reflection node selects this exact producer-owned surrogate before validation of its actual Id fields. */ => new ProblemSurrogateConverter().ConvertToSurrogate(problem) /* KLD0042: The actual producer ProblemSurrogate struct is boxed only for validation of its current Id fields and extensions. */,
            CommandMetadata metadata /* KLD0042: The runtime reflection node selects this exact producer-owned surrogate before validation of its actual Id fields. */ => new CommandMetadataSurrogateConverter().ConvertToSurrogate(metadata) /* KLD0042: The actual producer CommandMetadataSurrogate struct is boxed only for validation of its current Id fields. */,
            _ => null
        };
        return surrogate is not null;
    }

    private static ResultTSurrogate<object> /* KLD0041: The current stored result format uses this exact producer-owned surrogate, including its erased payload slot. */ ProjectResult(Result<object> /* KLD0041: This method checks the existing stored-result wire boundary rather than introducing an erased command reply. */ result)
    {
        if (result.IsSuccess ? result.Problem is not null : result.Value /* KLD0041: A failed stored result must have no value in its existing erased payload field. */ is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        if (result.IsFailed)
        {
            _ = Errors.Code(result.Problem).Value;
        }
        return new ResultTSurrogateConverter<object>() /* KLD0041: The registered producer converter is closed over the existing stored-result object payload schema. */.ConvertToSurrogate(result) /* KLD0041: Validation inspects the exact generated surrogate fields selected by this producer converter. */;
    }
}
