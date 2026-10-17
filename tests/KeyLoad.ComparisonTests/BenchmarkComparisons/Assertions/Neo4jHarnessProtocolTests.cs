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
        await Assert.That(Neo4jQueryResponse.ReadNativeErrorCode(duplicateCreate) == nativeCode).IsTrue();
        await VerifyHttpResponsesAsync(successfulCreate, duplicateCreate, nativeCode);
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
        await AssertMalformedErrorsAsync("{\"errors\":[{\"code\":\"Neo.ClientError.Schema.Schéma\"}]}");
        await AssertMalformedErrorsAsync("{\"errors\":[{\"code\":\"Neo.ClientError.Schema." + new string('A', Neo4jHarnessConstants.MaximumNativeCodeLength) + "\"}]}");
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

    private static async Task VerifyHttpResponsesAsync(JsonElement successful, JsonElement failed, string nativeCode)
    {
        Neo4jQueryResponse.ValidateResponse(Neo4jHarnessConstants.AcceptedStatusCode, successful);
        foreach (var status in new[] { Neo4jHarnessConstants.AcceptedStatusCode, Neo4jHarnessConstants.QueryErrorStatusCode })
        {
            await AssertInvalidAsync(() => Neo4jQueryResponse.ValidateResponse(status, failed), "Neo4j:" + nativeCode);
        }

        await AssertInvalidAsync(() => Neo4jQueryResponse.ValidateResponse(Neo4jHarnessConstants.QueryErrorStatusCode, successful), InvalidResponse);
        foreach (var status in Neo4jHarnessConstants.UnexpectedQueryStatuses())
        {
            await AssertInvalidAsync(() => Neo4jQueryResponse.ValidateResponse(status, successful), InvalidResponse);
            await AssertInvalidAsync(() => Neo4jQueryResponse.ValidateResponse(status, failed), InvalidResponse);
        }

        foreach (var body in new[]
        {
            "{}", "{\"errors\":[]}", "{\"errors\":null}", "{\"errors\":{}}", "[]", "null",
            "{\"data\":null}", "{\"data\":{}}", "{\"data\":{},\"data\":{}}",
            "{\"data\":{\"fields\":[]}}", "{\"data\":{\"values\":[]}}",
            "{\"data\":{\"fields\":[1],\"values\":[]}}", "{\"data\":{\"fields\":null,\"values\":[]}}",
            "{\"data\":{\"fields\":[],\"values\":null}}", "{\"data\":{\"fields\":[],\"values\":{}}}",
            "{\"data\":{\"fields\":[],\"values\":[null]}}", "{\"data\":{\"fields\":[],\"values\":[[1]]}}",
            "{\"data\":{\"fields\":[\"n\"],\"values\":[[]]}}",
            "{\"data\":{\"fields\":[],\"fields\":[],\"values\":[]}}",
            "{\"data\":{\"fields\":[],\"values\":[],\"values\":[]}}"
        })
        {
            using var document = JsonDocument.Parse(body);
            await AssertInvalidAsync(() => Neo4jQueryResponse.ValidateResponse(Neo4jHarnessConstants.AcceptedStatusCode, document.RootElement), InvalidResponse);
            await AssertInvalidAsync(() => Neo4jQueryResponse.ValidateResponse(Neo4jHarnessConstants.QueryErrorStatusCode, document.RootElement), InvalidResponse);
        }

        using var malformed = JsonDocument.Parse("{\"errors\":[{\"code\":\"" + nativeCode + "\"},{\"code\":null}]}");
        foreach (var status in new[] { Neo4jHarnessConstants.AcceptedStatusCode, Neo4jHarnessConstants.QueryErrorStatusCode })
        {
            await AssertInvalidAsync(() => Neo4jQueryResponse.ValidateResponse(status, malformed.RootElement), InvalidResponse);
        }
    }
}
