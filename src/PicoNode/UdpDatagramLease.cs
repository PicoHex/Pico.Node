namespace PicoNode;

internal sealed class UdpDatagramLease(byte[] buffer, int count, IPEndPoint remoteEndPoint)
    : IDisposable
{
    private int _disposed;

    public byte[] Buffer { get; } = buffer;

    public int Count { get; } = count;

    public IPEndPoint RemoteEndPoint { get; } = remoteEndPoint;

    public ArraySegment<byte> Datagram { get; } = new(buffer, 0, count);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        ArrayPool<byte>.Shared.Return(Buffer, clearArray: false);
    }
}
