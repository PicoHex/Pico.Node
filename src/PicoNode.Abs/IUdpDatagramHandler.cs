namespace PicoNode.Abs;

public interface IUdpDatagramHandler
{
    Task OnDatagramAsync(
        IUdpDatagramContext context,
        ArraySegment<byte> datagram,
        CancellationToken cancellationToken
    );
}
