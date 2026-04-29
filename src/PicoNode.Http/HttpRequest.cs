namespace PicoNode.Http;

public sealed class HttpRequest
{
    public required string Method { get; init; }

    public required string Target { get; init; }

    public string Path { get; init; } = string.Empty;

    public string QueryString { get; init; } = string.Empty;

    public HttpVersion Version { get; init; } = HttpVersion.Http11;

    public IReadOnlyList<KeyValuePair<string, string>> HeaderFields { get; init; } = [];

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ReadOnlyMemory<byte> Body { get; init; } = ReadOnlyMemory<byte>.Empty;

    public Stream CreateBodyStream() => new MemoryStream(Body.ToArray(), writable: false);
}
