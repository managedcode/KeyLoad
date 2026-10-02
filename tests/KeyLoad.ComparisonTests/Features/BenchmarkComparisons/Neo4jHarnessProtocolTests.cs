using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jHarnessProtocolTests
{
    private const string InvalidResponse = Neo4jHarnessConstants.InvalidQueryResponse;
    private const string EmptySchemaWrite = "{\"data\":{\"fields\":[],\"values\":[]},\"queryType\":\"s\",\"bookmarks\":[]}";

    public static async Task VerifyAsync(JsonElement successfulCreate, JsonElement duplicateCreate, string nativeCode)
    {
        await AssertNativeErrorAsync(duplicateCreate, nativeCode);
        await AssertConstraintAcknowledgementAsync(duplicateCreate, "Neo4j:" + nativeCode);
        await AssertNativeErrorAsync(successfulCreate, null);
        await AssertConstraintAcknowledgementAsync(successfulCreate, null);
        await AssertEmptyErrorsAcceptedAsync();
        await AssertInvalidErrorsAsync();
        await AssertInvalidConstraintAcknowledgementsAsync();
        await AssertDuplicateCriticalPropertiesAsync(nativeCode);
        await Assert.That(IsNativeCode(nativeCode)).IsTrue();
    }

    private static async Task AssertNativeErrorAsync(JsonElement response, string? expectedCode)
    {
        ComparisonFailureException? failure = null;
        try
        {
            Neo4jQueryResponse.ValidateErrors(response);
        }
        catch (ComparisonFailureException error)
        {
            failure = error;
        }

        var expectedMessage = expectedCode is null ? null : "Neo4j:" + expectedCode;
        await Assert.That(failure?.Message == expectedMessage).IsTrue();
    }

    private static async Task AssertConstraintAcknowledgementAsync(JsonElement response, string? expectedFailure)
    {
        ComparisonFailureException? failure = null;
        try
        {
            Neo4jQueryResponse.ValidateConstraintCreation(response);
        }
        catch (ComparisonFailureException error)
        {
            failure = error;
        }

        await Assert.That(failure?.Message == expectedFailure).IsTrue();
    }

    private static async Task AssertInvalidErrorsAsync()
    {
        await AssertMalformedErrorsAsync("{\"errors\":null}");
        await AssertMalformedErrorsAsync("{\"errors\":{}}");
        await AssertMalformedErrorsAsync("{\"errors\":[null]}");
        await AssertMalformedErrorsAsync("{\"errors\":[{}]}");
        await AssertMalformedErrorsAsync("{\"errors\":[{\"code\":null}]}");
        await AssertMalformedErrorsAsync("{\"errors\":[{\"code\":\"\"}]}");
        await AssertMalformedErrorsAsync("{\"errors\":[{\"code\":\"Neo.ClientError.Schema.Bad-Code\"}]}");
    }

    private static async Task AssertEmptyErrorsAcceptedAsync()
    {
        await AssertNoNativeErrorAsync("{}");
        await AssertNoNativeErrorAsync("{\"errors\":[]}");
    }

    private static async Task AssertInvalidConstraintAcknowledgementsAsync()
    {
        await AssertMalformedConstraintAsync("{}");
        await AssertMalformedConstraintAsync("{\"data\":null,\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[]},\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[]},\"queryType\":\"s\"}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":null,\"values\":[]},\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":null},\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[\"n\"],\"values\":[]},\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[[]]},\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[]},\"queryType\":\"rw\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[]},\"queryType\":\"s\",\"bookmarks\":null}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[]},\"queryType\":\"s\",\"bookmarks\":[1]}");
    }

    private static async Task AssertDuplicateCriticalPropertiesAsync(string nativeCode)
    {
        await AssertMalformedErrorsAsync("{\"errors\":[{\"code\":\"" + nativeCode + "\"}],\"errors\":[]}");
        await AssertMalformedErrorsAsync("{\"errors\":[],\"errors\":[{\"code\":\"" + nativeCode + "\"}]}");
        await AssertMalformedErrorsAsync("{\"errors\":[{\"code\":\"" + nativeCode + "\",\"code\":\"" + nativeCode + "\"}]}");
        await AssertMalformedErrorsAsync("{\"errors\":[{\"code\":\"" + nativeCode + "\",\"code\":\"Neo.ClientError.General.UnknownError\"}]}");
        await AssertMalformedConstraintAsync("{\"errors\":[],\"errors\":[]," + EmptySchemaWrite[1..]);
        await AssertMalformedConstraintAsync("{\"errors\":[{\"code\":\"" + nativeCode + "\"}],\"errors\":[]," + EmptySchemaWrite[1..]);
        await AssertMalformedConstraintAsync("{\"data\":{},\"data\":{},\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":null,\"data\":{},\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"fields\":[],\"values\":[]},\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"fields\":[\"n\"],\"values\":[]},\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[],\"values\":[]},\"queryType\":\"s\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[]},\"queryType\":\"s\",\"queryType\":\"rw\",\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[]},\"queryType\":\"s\",\"bookmarks\":[],\"bookmarks\":[]}");
        await AssertMalformedConstraintAsync("{\"data\":{\"fields\":[],\"values\":[]},\"queryType\":\"s\",\"bookmarks\":[],\"bookmarks\":[\"opaque\"]}");
    }

    private static async Task AssertNoNativeErrorAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        ComparisonFailureException? failure = null;
        try
        {
            Neo4jQueryResponse.ValidateErrors(document.RootElement);
        }
        catch (ComparisonFailureException error)
        {
            failure = error;
        }

        await Assert.That(failure is null).IsTrue();
    }

    private static async Task AssertMalformedErrorsAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        await AssertInvalidAsync(() => Neo4jQueryResponse.ValidateErrors(document.RootElement), InvalidResponse);
    }

    private static async Task AssertMalformedConstraintAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        await AssertInvalidAsync(() => Neo4jQueryResponse.ValidateConstraintCreation(document.RootElement), InvalidResponse);
    }

    private static async Task AssertInvalidAsync(Action validation, string expected)
    {
        ComparisonFailureException? failure = null;
        try
        {
            validation();
        }
        catch (ComparisonFailureException error)
        {
            failure = error;
        }

        await Assert.That(failure?.Message == expected).IsTrue();
    }

    private static bool IsNativeCode(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 4 && parts[0] == "Neo" && parts.All(IsIdentifier);
    }

    private static bool IsIdentifier(string value) => value.Length > 0 && IsAsciiLetter(value[0])
        && value.All(character => IsAsciiLetter(character) || character is >= '0' and <= '9' or '_');

    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
