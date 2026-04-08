using System.Text;
using PicoNode.Http.Internal;

namespace PicoNode.Http.Tests;

public sealed class HttpRequestParserTests
{
    [Test]
    public async Task Parse_successfully_materializes_one_request_and_exact_consumed_position()
    {
        var buffer = CreateSequence(
            Encoding.ASCII.GetBytes("POST /submit HTTP/1.1\r\nHost: Example.com\r\nContent-Length: 5\r\n\r\nhe"),
            Encoding.ASCII.GetBytes("lloNEXT")
        );

        var result = HttpRequestParser.Parse(
            buffer,
            new HttpConnectionHandlerOptions
            {
                RequestHandler = static (_, _) => default,
            }
        );

        await Assert.That(result.Status).IsEqualTo(HttpRequestParseStatus.Success);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Request).IsNotNull();
        var request = result.Request ?? throw new InvalidOperationException("Request should be present.");
        await Assert.That(request.Method).IsEqualTo("POST");
        await Assert.That(request.Target).IsEqualTo("/submit");
        await Assert.That(request.Version).IsEqualTo("HTTP/1.1");
        await Assert.That(request.Headers["host"]).IsEqualTo("Example.com");
        await Assert.That(request.Headers["content-length"]).IsEqualTo("5");
        await Assert.That(Encoding.ASCII.GetString(request.Body.ToArray())).IsEqualTo("hello");
        await Assert.That(Encoding.ASCII.GetString(buffer.Slice(result.Consumed).ToArray())).IsEqualTo("NEXT");
    }

    [Test]
    public async Task Parse_preserves_repeated_request_headers_and_combines_lookup_values()
    {
        var buffer = CreateSequence(
            Encoding.ASCII.GetBytes(
                "GET / HTTP/1.1\r\nAccept: text/plain\r\naccept: text/html\r\nConnection: keep-alive\r\nConnection: close\r\n\r\n"
            )
        );

        var result = HttpRequestParser.Parse(
            buffer,
            new HttpConnectionHandlerOptions
            {
                RequestHandler = static (_, _) => default,
            }
        );

        await Assert.That(result.Status).IsEqualTo(HttpRequestParseStatus.Success);
        await Assert.That(result.Request).IsNotNull();

        var request = result.Request ?? throw new InvalidOperationException("Request should be present.");
        await Assert.That(request.HeaderFields.Count).IsEqualTo(4);
        await Assert.That(request.HeaderFields[0]).IsEqualTo(new KeyValuePair<string, string>("Accept", "text/plain"));
        await Assert.That(request.HeaderFields[1]).IsEqualTo(new KeyValuePair<string, string>("accept", "text/html"));
        await Assert.That(request.Headers["Accept"]).IsEqualTo("text/plain, text/html");
        await Assert.That(request.Headers["Connection"]).IsEqualTo("keep-alive, close");
    }

    [Test]
    public async Task Parse_incomplete_body_consumes_nothing()
    {
        var buffer = CreateSequence(
            Encoding.ASCII.GetBytes("POST /submit HTTP/1.1\r\nContent-Length: 5\r\n\r\nhe")
        );

        var result = HttpRequestParser.Parse(
            buffer,
            new HttpConnectionHandlerOptions
            {
                RequestHandler = static (_, _) => default,
            }
        );

        await Assert.That(result.Status).IsEqualTo(HttpRequestParseStatus.Incomplete);
        await Assert.That(result.Request).IsNull();
        await Assert.That(result.Error).IsNull();
        await Assert.That(Encoding.ASCII.GetString(buffer.Slice(result.Consumed).ToArray()))
            .IsEqualTo("POST /submit HTTP/1.1\r\nContent-Length: 5\r\n\r\nhe");
    }

    [Test]
    public async Task Parse_rejects_transfer_encoding()
    {
        var buffer = CreateSequence(
            Encoding.ASCII.GetBytes("POST /submit HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n")
        );

        var result = HttpRequestParser.Parse(
            buffer,
            new HttpConnectionHandlerOptions
            {
                RequestHandler = static (_, _) => default,
            }
        );

        await Assert.That(result.Status).IsEqualTo(HttpRequestParseStatus.Rejected);
        await Assert.That(result.Error).IsEqualTo(HttpRequestParseError.UnsupportedFraming);
        await Assert.That(result.Request).IsNull();
        await Assert.That(buffer.Slice(result.Consumed).Length).IsEqualTo(buffer.Length);
    }

    [Test]
    public async Task Parse_rejects_duplicate_content_length()
    {
        var buffer = CreateSequence(
            Encoding.ASCII.GetBytes(
                "POST /submit HTTP/1.1\r\nContent-Length: 5\r\ncontent-length: 5\r\n\r\nhello"
            )
        );

        var result = HttpRequestParser.Parse(
            buffer,
            new HttpConnectionHandlerOptions
            {
                RequestHandler = static (_, _) => default,
            }
        );

        await Assert.That(result.Status).IsEqualTo(HttpRequestParseStatus.Rejected);
        await Assert.That(result.Error).IsEqualTo(HttpRequestParseError.DuplicateContentLength);
        await Assert.That(result.Request).IsNull();
    }

    [Test]
    public async Task Parse_rejects_malformed_request_line()
    {
        var buffer = CreateSequence(Encoding.ASCII.GetBytes("POST /submit\r\nContent-Length: 5\r\n\r\nhello"));

        var result = HttpRequestParser.Parse(
            buffer,
            new HttpConnectionHandlerOptions
            {
                RequestHandler = static (_, _) => default,
            }
        );

        await Assert.That(result.Status).IsEqualTo(HttpRequestParseStatus.Rejected);
        await Assert.That(result.Error).IsEqualTo(HttpRequestParseError.InvalidRequestLine);
        await Assert.That(result.Request).IsNull();
    }

    [Test]
    public async Task Parse_rejects_size_limit_violations()
    {
        var buffer = CreateSequence(Encoding.ASCII.GetBytes("GET /very-long-path HTTP/1.1\r\n\r\n"));

        var result = HttpRequestParser.Parse(
            buffer,
            new HttpConnectionHandlerOptions
            {
                RequestHandler = static (_, _) => default,
                MaxRequestBytes = 8,
            }
        );

        await Assert.That(result.Status).IsEqualTo(HttpRequestParseStatus.Rejected);
        await Assert.That(result.Error).IsEqualTo(HttpRequestParseError.RequestTooLarge);
        await Assert.That(result.Request).IsNull();
    }

    [Test]
    public async Task Parse_reports_invalid_headers_when_header_line_missing_carriage_return()
    {
        var buffer = CreateSequence(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\n\r\n")
        );

        var result = HttpRequestParser.Parse(
            buffer,
            new HttpConnectionHandlerOptions
            {
                RequestHandler = static (_, _) => default,
            }
        );

        await Assert.That(result.Status).IsEqualTo(HttpRequestParseStatus.Rejected);
        await Assert.That(result.Error).IsEqualTo(HttpRequestParseError.InvalidHeader);
    }

    private static ReadOnlySequence<byte> CreateSequence(params ReadOnlyMemory<byte>[] segments)
    {
        if (segments.Length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        var first = new BufferSegment(segments[0]);
        var last = first;

        for (var index = 1; index < segments.Length; index++)
        {
            last = last.Append(segments[index]);
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length,
            };

            Next = segment;
            return segment;
        }
    }
}
