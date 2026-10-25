using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Surrogates;

namespace KeyLoad.InternalSerialization;

// Inspect the selected library's actual generated surrogates; do not mirror their wire contracts.
internal static class CommunicationContracts
{
    internal static bool TryProject(object value, out object? surrogate)
    {
        surrogate = value switch
        {
            Command<DatabaseCommand> command => new CommandTSurrogateConverter<DatabaseCommand>().ConvertToSurrogate(command),
            Result<object> result => ProjectResult(result),
            Problem problem => new ProblemSurrogateConverter().ConvertToSurrogate(problem),
            CommandMetadata metadata => new CommandMetadataSurrogateConverter().ConvertToSurrogate(metadata),
            _ => null
        };
        return surrogate is not null;
    }

    private static ResultTSurrogate<object> ProjectResult(Result<object> result)
    {
        if (result.IsSuccess ? result.Problem is not null : result.Value is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        if (result.IsFailed)
        {
            _ = Errors.Code(result.Problem).Value;
        }
        return new ResultTSurrogateConverter<object>().ConvertToSurrogate(result);
    }
}
