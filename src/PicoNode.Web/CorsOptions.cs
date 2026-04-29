namespace PicoNode.Web;

public sealed class CorsOptions
{
    private string? _allowedMethodsHeader;
    private string? _allowedHeadersHeader;
    private string? _exposedHeadersHeader;

    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    public IReadOnlyList<string> AllowedMethods { get; init; } =
        ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"];

    public IReadOnlyList<string> AllowedHeaders { get; init; } = ["Content-Type", "Authorization"];

    public IReadOnlyList<string> ExposedHeaders { get; init; } = [];

    public bool AllowCredentials { get; init; }

    public int? MaxAge { get; init; }

    internal string AllowedMethodsHeader =>
        _allowedMethodsHeader ??= string.Join(", ", AllowedMethods);

    internal string AllowedHeadersHeader =>
        _allowedHeadersHeader ??= string.Join(", ", AllowedHeaders);

    internal string ExposedHeadersHeader =>
        _exposedHeadersHeader ??= string.Join(", ", ExposedHeaders);
}
