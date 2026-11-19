using System.Buffers;
using System.Net;
using System.Text;
using EdiFabric.Api.Azure;
using EdiFabric.Native.X12;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public class EdiFunctions
{
    private const int ChunkSize = 81920;
    // The request limits of ediFabric Cloud v3: 5 MB of X12 for parse, 50 MB of JSON for build.
    private const int ParseRequestLimit = 5 * 1024 * 1024;
    private const int BuildRequestLimit = 50 * 1024 * 1024;
    private const string OffsetHeader = "X-EdiFabric-Offset";
    private readonly string _noData = "No data in request body.";

    public async Task<HttpResponseData> Read(HttpRequestData req, ILogger logger)
    {
        try
        {
            using var body = await ReadBodyAsync(req);
            if (body.IsEmpty)
                return await NoDataAsync(req, logger);

            var charSet = req.GetReadParams().CharSet;
            var edi = IsUtf8(charSet) ? body.Memory : Transcode(body.Span, charSet!);
            var (output, length, offset) = SerialGate.Run(GetApiKey(req), () => EdiFabricX12.ParseUtf8(edi.Span, ParseMode.Json));

            return await JsonResponseAsync(req, output.AsMemory(0, offset > 0 ? offset : length));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.ToString());
            return await req.BuildErrorResponse(ex);
        }
    }

    public async Task<HttpResponseData> Write(HttpRequestData req, ILogger logger)
    {
        try
        {
            using var body = await ReadBodyAsync(req);
            if (body.IsEmpty)
                return await NoDataAsync(req, logger);

            var writeParams = req.GetWriteParams();
            var (output, length) = SerialGate.Run(GetApiKey(req), () => EdiFabricX12.BuildUtf8(WithoutBom(body.Span), writeParams.Postfix));

            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", writeParams.ToContentType());
            await res.Body.WriteAsync(output.AsMemory(0, length));
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.ToString());
            return await req.BuildErrorResponse(ex);
        }
    }

    public async Task<HttpResponseData> Validate(HttpRequestData req, ILogger logger)
    {
        try
        {
            using var body = await ReadBodyAsync(req);
            if (body.IsEmpty)
                return await NoDataAsync(req, logger);

            var config = req.GetValidateParams().ToConfig();
            var (output, length, offset) = SerialGate.Run(GetApiKey(req), () => EdiFabricX12.ParseUtf8(ToEdi(req, body).Span, ParseMode.JsonValidate, config));

            return await JsonResponseAsync(req, Report(output, length, offset));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.ToString());
            return await req.BuildErrorResponse(ex);
        }
    }

    public async Task<HttpResponseData> Ack(HttpRequestData req, ILogger logger)
    {
        try
        {
            using var body = await ReadBodyAsync(req);
            if (body.IsEmpty)
                return await NoDataAsync(req, logger);

            var config = req.GetAckParams().ToConfig();
            var (output, length, offset) = SerialGate.Run(GetApiKey(req), () => EdiFabricX12.ParseUtf8(ToEdi(req, body).Span, ParseMode.JsonValidateAck, config));

            return await JsonResponseAsync(req, Report(output, length, offset));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.ToString());
            return await req.BuildErrorResponse(ex);
        }
    }

    /// <summary>
    /// ediFabric Cloud v3 Parse X12: a raw X12 file in, its JSON out, with the validation report in mode 2 and the
    /// acknowledgment in mode 3, in one call. In modes 2 and 3 the body is two JSON documents back to back; the report
    /// starts at the byte offset in the X-EdiFabric-Offset header.
    /// </summary>
    public async Task<HttpResponseData> Parse(HttpRequestData req, ILogger logger)
    {
        try
        {
            if (!HasContentType(req, "application/octet-stream"))
                return await req.BuildErrorResponse(HttpStatusCode.UnsupportedMediaType, "The Content-Type must be application/octet-stream.");

            using var body = await ReadBodyAsync(req, ParseRequestLimit);
            if (body.IsEmpty)
                return await NoDataAsync(req, logger);

            var parseParams = req.GetParseParams();
            var mode = parseParams.ToMode();
            var edi = IsUtf8(parseParams.CharSet) ? body.Memory : Transcode(body.Span, parseParams.CharSet!);
            var (output, length, offset) = SerialGate.Run(GetApiKey(req), () => EdiFabricX12.ParseUtf8(edi.Span, mode, parseParams.Config));

            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", mode == ParseMode.Json ? "application/json; charset=utf-8" : "application/octet-stream");
            res.Headers.Add(OffsetHeader, (mode == ParseMode.Json ? 0 : offset).ToString());
            await res.Body.WriteAsync(output.AsMemory(0, length));
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.ToString());
            return await req.BuildErrorResponse(ex);
        }
    }

    /// <summary>
    /// ediFabric Cloud v3 Build X12: an X12Interchange, or an array of them, in the v3 JSON format, out as raw X12.
    /// The segments are written as they are, so the JSON must include SE, GE and IEA with the correct counts.
    /// </summary>
    public async Task<HttpResponseData> Build(HttpRequestData req, ILogger logger)
    {
        try
        {
            if (!HasContentType(req, "application/json"))
                return await req.BuildErrorResponse(HttpStatusCode.UnsupportedMediaType, "The Content-Type must be application/json.");

            using var body = await ReadBodyAsync(req, BuildRequestLimit);
            if (body.IsEmpty)
                return await NoDataAsync(req, logger);

            var postfix = req.GetBuildParams().Postfix;
            var (output, length) = SerialGate.Run(GetApiKey(req), () => EdiFabricX12.BuildUtf8(WithoutBom(body.Span), postfix));

            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", "application/octet-stream; charset=utf-8");
            await res.Body.WriteAsync(output.AsMemory(0, length));
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.ToString());
            return await req.BuildErrorResponse(ex);
        }
    }

    private static bool HasContentType(HttpRequestData req, string mediaType)
    {
        var contentType = req.GetContentType();
        return contentType != null && contentType.Split(';')[0].Trim().Equals(mediaType, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseData> NoDataAsync(HttpRequestData req, ILogger logger)
    {
        logger.LogError(_noData);
        return await req.BuildErrorResponse(HttpStatusCode.BadRequest, _noData);
    }

    private static async Task<HttpResponseData> JsonResponseAsync(HttpRequestData req, ReadOnlyMemory<byte> json)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await res.Body.WriteAsync(json);
        return res;
    }

    // In modes 2 and 3 the native output is the transaction JSON followed by the report; the report starts at the offset.
    private static ReadOnlyMemory<byte> Report(byte[] output, int length, int offset)
        => offset > 0 ? output.AsMemory(offset, length - offset) : ReadOnlyMemory<byte>.Empty;

    // The serial of the request, as in ediFabric Cloud, or the configured one. SerialGate runs the native calls under it.
    private static string GetApiKey(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var apiKeys) && !string.IsNullOrWhiteSpace(apiKeys.FirstOrDefault()))
            return apiKeys.First().Trim();

        return Configuration.ApiKey;
    }

    // Validate and ack also take transaction JSON, which is built into X12 first.
    private static ReadOnlyMemory<byte> ToEdi(HttpRequestData req, RequestBody body)
    {
        var contentType = req.GetContentType();
        if (contentType != null && contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var (output, length) = EdiFabricX12.BuildUtf8(WithoutBom(body.Span));
            return output.AsMemory(0, length);
        }

        return body.Memory;
    }

    // The ASP.NET Core request stream is forward-only (Length and Seek throw), and the native
    // parser needs the whole interchange in one contiguous buffer, so the body is read once
    // into a pooled buffer sized from Content-Length when the client sends it.
    // Content-Length is optional, so a limit is also enforced on the bytes actually read.
    private static async Task<RequestBody> ReadBodyAsync(HttpRequestData req, int limit = int.MaxValue)
    {
        var stream = req.Body;
        var declared = GetContentLength(req);
        if (stream == null || declared == 0)
            return default;

        if (declared > limit)
            throw new RequestTooLargeException(limit);

        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(declared > 0 && declared <= Array.MaxLength ? (int)declared : ChunkSize);
        var length = 0;

        try
        {
            while (length != declared)
            {
                if (length == buffer.Length)
                    buffer = Grow(buffer, length);

                var read = await stream.ReadAsync(buffer.AsMemory(length));
                if (read == 0)
                    break;

                length += read;
                if (length > limit)
                    throw new RequestTooLargeException(limit);
            }
        }
        catch
        {
            pool.Return(buffer);
            throw;
        }

        if (length == 0)
        {
            pool.Return(buffer);
            return default;
        }

        return new RequestBody(buffer, length);
    }

    private static byte[] Grow(byte[] buffer, int length)
    {
        var size = (int)Math.Min((long)buffer.Length * 2, Array.MaxLength);
        if (size <= buffer.Length)
            throw new InsufficientMemoryException("Request body is too large.");

        var bigger = ArrayPool<byte>.Shared.Rent(size);
        buffer.AsSpan(0, length).CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(buffer);
        return bigger;
    }

    private static long? GetContentLength(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("Content-Length", out var values) && long.TryParse(values.FirstOrDefault(), out var length))
            return length;

        return null;
    }

    private static bool IsUtf8(string? charSet)
        => string.IsNullOrEmpty(charSet)
            || charSet.Equals("utf-8", StringComparison.OrdinalIgnoreCase)
            || charSet.Equals("utf8", StringComparison.OrdinalIgnoreCase);

    private static byte[] Transcode(ReadOnlySpan<byte> bytes, string charSet)
        => Encoding.UTF8.GetBytes(GetEncoding(charSet).GetString(bytes));

    // An unknown charSet is the caller's mistake (400), as in ediFabric Cloud.
    private static Encoding GetEncoding(string charSet)
    {
        try
        {
            return Encoding.GetEncoding(charSet);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"Invalid charSet '{charSet}'.", ex);
        }
    }

    private static ReadOnlySpan<byte> WithoutBom(ReadOnlySpan<byte> bytes)
        => bytes.StartsWith(Encoding.UTF8.Preamble) ? bytes[Encoding.UTF8.Preamble.Length..] : bytes;

    private readonly struct RequestBody : IDisposable
    {
        private readonly byte[]? _buffer;
        private readonly int _length;

        public RequestBody(byte[] buffer, int length)
        {
            _buffer = buffer;
            _length = length;
        }

        public bool IsEmpty => _length == 0;

        public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

        public ReadOnlyMemory<byte> Memory => _buffer.AsMemory(0, _length);

        public void Dispose()
        {
            if (_buffer != null)
                ArrayPool<byte>.Shared.Return(_buffer);
        }
    }
}
