using System.Net;
using EdiFabric.Native.X12;
using Microsoft.Azure.Functions.Worker.Http;

static class ErrorHandler
{
    public static async Task<HttpResponseData> BuildErrorResponse(this HttpRequestData req, HttpStatusCode statusCode, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new
        {
            Code = (int)statusCode,
            Details = new List<string> { message }
        });
        return response;
    }

    public static async Task<HttpResponseData> BuildErrorResponse(this HttpRequestData req, Exception ex)
    {
        return await req.BuildErrorResponse(StatusCodeFor(ex), ex.Message);
    }

    private static HttpStatusCode StatusCodeFor(Exception ex)
    {
        if (ex is InvalidDataException)
            return HttpStatusCode.BadRequest;

        if (ex is RequestTooLargeException)
            return HttpStatusCode.RequestEntityTooLarge;

        if (ex is EdiFabricException ediEx)
            return (HttpStatusCode)StatusCodeFor(ediEx.Code);

        return HttpStatusCode.InternalServerError;
    }

    /// <summary>
    /// The HTTP status for an ediFabric Native error code, as ediFabric Cloud answers:
    /// 400 for the request, 401 for the serial, 403 for the plan, 429 for the rate, 503 when the license server can't be reached.
    /// </summary>
    private static int StatusCodeFor(int code) => code switch
    {
        (int)EdiFabricErrorCode.IncorrectInput or
        (int)EdiFabricErrorCode.MapNotSet or
        (int)EdiFabricErrorCode.IncorrectMode or
        (int)EdiFabricErrorCode.ConfigDeserialization or
        (int)EdiFabricErrorCode.SplitSegmentIdMissing => 400,
        (int)EdiFabricErrorCode.IncorrectToken or
        (int)EdiFabricErrorCode.IncorrectSerial or
        (int)EdiFabricErrorCode.TokenExpired or
        (int)EdiFabricErrorCode.TokenMissing or
        (int)EdiFabricErrorCode.IncorrectLicense => 401,
        (int)EdiFabricErrorCode.AppVersionExceeded or
        (int)EdiFabricErrorCode.MaxLicensesExceeded or
        (int)EdiFabricErrorCode.LicenseQuotaExceeded => 403,
        (int)EdiFabricErrorCode.RateExceeded => 429,
        // 640: the license server can't be reached and there is no valid cached token.
        (int)EdiFabricErrorCode.NoConnection or 640 => 503,
        _ => 500,
    };
}

/// <summary>The request body is larger than the operation accepts (HTTP 413).</summary>
sealed class RequestTooLargeException(int limit)
    : Exception($"The request body is larger than {limit / (1024 * 1024)} MB.");
