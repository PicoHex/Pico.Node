namespace PicoNode.Tests;

public sealed class UdpDatagramLeaseTests
{
    [Test]
    public async Task Datagram_returns_buffer_segment_for_count()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(5);
        buffer[0] = 1;
        buffer[1] = 2;
        buffer[2] = 3;
        buffer[3] = 4;
        buffer[4] = 5;

        using var lease = new UdpDatagramLease(buffer, 3, new IPEndPoint(IPAddress.Loopback, 4567));

        await Assert.That(lease.Count).IsEqualTo(3);
        await Assert.That(lease.RemoteEndPoint).IsEqualTo(new IPEndPoint(IPAddress.Loopback, 4567));
        await Assert.That(lease.Datagram.Array).IsSameReferenceAs(buffer);
        await Assert.That(lease.Datagram.Offset).IsEqualTo(0);
        await Assert.That(lease.Datagram.Count).IsEqualTo(3);
        await Assert.That(lease.Datagram.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Test]
    public async Task Dispose_can_be_called_multiple_times_without_throwing()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(3);
        buffer[0] = 9;
        buffer[1] = 8;
        buffer[2] = 7;

        var lease = new UdpDatagramLease(buffer, 2, new IPEndPoint(IPAddress.Loopback, 1234));

        lease.Dispose();
        lease.Dispose();

        await Assert.That(lease.Datagram.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Datagram_remains_stable_after_dispose_calls()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4);
        buffer[0] = 4;
        buffer[1] = 3;
        buffer[2] = 2;
        buffer[3] = 1;

        var lease = new UdpDatagramLease(buffer, 3, new IPEndPoint(IPAddress.Loopback, 7890));

        var datagramBeforeDispose = lease.Datagram;

        lease.Dispose();
        lease.Dispose();

        await Assert.That(datagramBeforeDispose.Array).IsSameReferenceAs(buffer);
        await Assert.That(datagramBeforeDispose.Offset).IsEqualTo(0);
        await Assert.That(datagramBeforeDispose.Count).IsEqualTo(3);
    }
}
