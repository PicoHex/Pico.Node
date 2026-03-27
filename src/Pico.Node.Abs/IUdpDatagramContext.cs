namespace Pico.Node.Abs;

public interface IUdpDatagramContext
{
    IPEndPoint RemoteEndPoint { get; }
    Task SendAsync(ArraySegment<byte> datagram, CancellationToken cancellationToken = default);
}
