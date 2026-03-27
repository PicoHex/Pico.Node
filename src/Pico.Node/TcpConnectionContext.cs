namespace Pico.Node;

public sealed class TcpConnectionContext : ITcpConnectionContext
{
    private readonly TcpConnection _connection;
    private readonly int _generation;

    internal TcpConnectionContext(
        TcpConnection connection,
        int generation,
        long id,
        IPEndPoint remoteEndPoint,
        DateTimeOffset connectedAtUtc
    )
    {
        _connection = connection;
        _generation = generation;
        Id = id;
        RemoteEndPoint = remoteEndPoint;
        ConnectedAtUtc = connectedAtUtc;
    }

    public long Id { get; }

    public IPEndPoint RemoteEndPoint { get; }

    public DateTimeOffset ConnectedAtUtc { get; }

    public DateTimeOffset LastActivityUtc => _connection.GetLastActivityUtc(_generation, ConnectedAtUtc);

    public Task SendAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default)
        => _connection.SendAsync(_generation, buffer, cancellationToken);

    public void Close() => _connection.Close(_generation);
}
