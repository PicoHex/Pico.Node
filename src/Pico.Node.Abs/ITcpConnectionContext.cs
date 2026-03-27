namespace Pico.Node.Abs;

public interface ITcpConnectionContext
{
    long Id { get; }
    IPEndPoint RemoteEndPoint { get; }
    DateTimeOffset ConnectedAtUtc { get; }
    DateTimeOffset LastActivityUtc { get; }
    Task SendAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default);
    void Close();
}
