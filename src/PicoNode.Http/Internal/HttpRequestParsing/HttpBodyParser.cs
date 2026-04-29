namespace PicoNode.Http.Internal.HttpRequestParsing;

internal static class HttpBodyParser
{
    public static HttpRequestParseResult ParseBody(
        ref HttpRequestParser.BufferReader reader,
        HttpConnectionHandlerOptions options,
        string method,
        string target,
        HttpVersion version,
        List<KeyValuePair<string, string>> headerFields,
        Dictionary<string, string> headers,
        long contentLength,
        bool expectsContinue
    )
    {
        var body = ReadOnlyMemory<byte>.Empty;

        if (contentLength <= 0)
            return HttpRequestParseResult.Success(
                new HttpRequest
                {
                    Method = method,
                    Target = target,
                    Version = version,
                    HeaderFields = headerFields,
                    Headers = headers,
                    Body = body,
                },
                reader.Position
            );

        if (
            contentLength > int.MaxValue
            || reader.ConsumedBytes + contentLength > options.MaxRequestBytes
        )
        {
            return HttpRequestParseResult.Rejected(
                reader.BufferStart,
                HttpRequestParseError.RequestTooLarge
            );
        }

        var bodyBytes = reader.SliceFromPosition();
        if (bodyBytes.Length < contentLength)
        {
            return HttpRequestParseResult.Incomplete(reader.BufferStart, expectsContinue);
        }

        var bodyArray = new byte[contentLength];
        bodyBytes.Slice(0, contentLength).CopyTo(bodyArray);
        reader.Advance(contentLength);
        body = bodyArray;

        return HttpRequestParseResult.Success(
            new HttpRequest
            {
                Method = method,
                Target = target,
                Version = version,
                HeaderFields = headerFields,
                Headers = headers,
                Body = body,
            },
            reader.Position
        );
    }

    public static HttpRequestParseResult ParseChunkedBody(
        ref HttpRequestParser.BufferReader reader,
        HttpConnectionHandlerOptions options,
        string method,
        string target,
        HttpVersion version,
        List<KeyValuePair<string, string>> headerFields,
        Dictionary<string, string> headers,
        bool expectsContinue
    )
    {
        var bodyWriter = new ArrayBufferWriter<byte>();
        var totalBodyLength = 0L;

        while (true)
        {
            if (
                !reader.TryReadLine(
                    options.MaxRequestBytes,
                    HttpRequestParseError.InvalidChunkedBody,
                    out var chunkSizeLine,
                    out var error
                )
            )
            {
                return error is null
                    ? HttpRequestParseResult.Incomplete(reader.BufferStart, expectsContinue)
                    : HttpRequestParseResult.Rejected(reader.BufferStart, error.Value);
            }

            var chunkSize = HttpParseHelpers.ParseChunkSize(chunkSizeLine);
            if (chunkSize < 0)
            {
                return HttpRequestParseResult.Rejected(
                    reader.BufferStart,
                    HttpRequestParseError.InvalidChunkedBody
                );
            }

            if (chunkSize == 0)
            {
                while (true)
                {
                    if (
                        !reader.TryReadLine(
                            options.MaxRequestBytes,
                            HttpRequestParseError.InvalidChunkedBody,
                            out var trailerLine,
                            out var trailerError
                        )
                    )
                    {
                        return trailerError is null
                            ? HttpRequestParseResult.Incomplete(reader.BufferStart, expectsContinue)
                            : HttpRequestParseResult.Rejected(
                                reader.BufferStart,
                                trailerError.Value
                            );
                    }

                    if (trailerLine.Length == 0)
                    {
                        break;
                    }
                }

                break;
            }

            totalBodyLength += chunkSize;
            if (
                totalBodyLength > int.MaxValue
                || reader.ConsumedBytes + totalBodyLength > options.MaxRequestBytes
            )
            {
                return HttpRequestParseResult.Rejected(
                    reader.BufferStart,
                    HttpRequestParseError.RequestTooLarge
                );
            }

            var remaining = reader.SliceFromPosition();
            if (remaining.Length < (long)chunkSize + 2)
            {
                return HttpRequestParseResult.Incomplete(reader.BufferStart, expectsContinue);
            }

            var crlfStart = remaining.Slice(chunkSize, 2);
            if (
                HttpParseHelpers.GetFirstByte(crlfStart) != (byte)'\r'
                || HttpParseHelpers.GetLastByte(crlfStart) != (byte)'\n'
            )
            {
                return HttpRequestParseResult.Rejected(
                    reader.BufferStart,
                    HttpRequestParseError.InvalidChunkedBody
                );
            }

            var span = bodyWriter.GetSpan(chunkSize);
            remaining.Slice(0, chunkSize).CopyTo(span);
            bodyWriter.Advance(chunkSize);

            reader.Advance(chunkSize + 2);
            reader.ConsumedBytes += chunkSize + 2;
        }

        var body = bodyWriter.WrittenSpan.ToArray();

        return HttpRequestParseResult.Success(
            new HttpRequest
            {
                Method = method,
                Target = target,
                Version = version,
                HeaderFields = headerFields,
                Headers = headers,
                Body = body,
            },
            reader.Position
        );
    }
}
