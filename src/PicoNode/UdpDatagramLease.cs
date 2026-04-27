namespace PicoNode;

internal sealed class UdpDatagramLease(byte[] buffer, int count, IPEndPoint remoteEndPoint)
    : IDisposable
{
    private bool _disposed;

    public byte[] Buffer { get; } = buffer;

    public int Count { get; } = count;

    public IPEndPoint RemoteEndPoint { get; } = remoteEndPoint;

    public ArraySegment<byte> Datagram { get; } = new(buffer, 0, count);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(Buffer, clearArray: false);
    }
}
