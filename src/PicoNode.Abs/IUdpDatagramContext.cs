namespace PicoNode.Abs;

public interface IUdpDatagramContext
{
    IPEndPoint RemoteEndPoint { get; }
    Task SendAsync(ArraySegment<byte> datagram, CancellationToken cancellationToken = default);
}
