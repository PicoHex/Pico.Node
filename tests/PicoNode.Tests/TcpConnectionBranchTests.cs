using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using PicoNode;
using PicoNode.Abs;

public sealed class TcpConnectionBranchTests
{
    [Test]
    public async Task MapSocketExceptionReason_returns_remote_closed_for_connection_reset()
    {
        var pair = await CreateConnectedSocketsAsync();
        try
        {
            var connection = CreateConnection(pair.Server);
            var result = InvokeMapSocketExceptionReason(
                connection,
                new SocketException((int)SocketError.ConnectionReset),
                TcpCloseReason.RemoteClosed
            );

            await Assert.That(result).IsEqualTo(TcpCloseReason.RemoteClosed);
        }
        finally
        {
            pair.Client.Dispose();
            pair.Server.Dispose();
        }
    }

    [Test]
    public async Task MapSocketExceptionReason_returns_local_close_after_connection_cancellation()
    {
        var pair = await CreateConnectedSocketsAsync();
        try
        {
            var connection = CreateConnection(pair.Server);
            await InvokeCancelConnectionAsync(connection);

            var result = InvokeMapSocketExceptionReason(
                connection,
                new SocketException((int)SocketError.OperationAborted),
                TcpCloseReason.RemoteClosed
            );

            await Assert.That(result).IsEqualTo(TcpCloseReason.LocalClose);
        }
        finally
        {
            pair.Client.Dispose();
            pair.Server.Dispose();
        }
    }

    [Test]
    public async Task MapSocketExceptionReason_returns_receive_fault_for_other_socket_errors()
    {
        var pair = await CreateConnectedSocketsAsync();
        try
        {
            var connection = CreateConnection(pair.Server);
            var result = InvokeMapSocketExceptionReason(
                connection,
                new SocketException((int)SocketError.NetworkDown),
                TcpCloseReason.RemoteClosed
            );

            await Assert.That(result).IsEqualTo(TcpCloseReason.ReceiveFault);
        }
        finally
        {
            pair.Client.Dispose();
            pair.Server.Dispose();
        }
    }

    [Test]
    public async Task MapSocketExceptionReason_preserves_local_close_for_other_socket_errors()
    {
        var pair = await CreateConnectedSocketsAsync();
        try
        {
            var connection = CreateConnection(pair.Server);
            var result = InvokeMapSocketExceptionReason(
                connection,
                new SocketException((int)SocketError.NetworkDown),
                TcpCloseReason.LocalClose
            );

            await Assert.That(result).IsEqualTo(TcpCloseReason.LocalClose);
        }
        finally
        {
            pair.Client.Dispose();
            pair.Server.Dispose();
        }
    }

    [Test]
    public async Task ShouldReportReceiveFault_returns_true_for_remote_closed_and_receive_fault()
    {
        await Assert.That(InvokeShouldReportReceiveFault(TcpCloseReason.RemoteClosed)).IsTrue();
        await Assert.That(InvokeShouldReportReceiveFault(TcpCloseReason.ReceiveFault)).IsTrue();
        await Assert.That(InvokeShouldReportReceiveFault(TcpCloseReason.LocalClose)).IsFalse();
    }

    private static TcpConnection CreateConnection(Socket serverSocket)
    {
        var node = new TcpNode(
            new TcpNodeOptions
            {
                Endpoint = new IPEndPoint(IPAddress.Loopback, 0),
                ConnectionHandler = new NoOpTcpHandler(),
            }
        );

        return new TcpConnection(node, serverSocket);
    }

    private static TcpCloseReason InvokeMapSocketExceptionReason(
        TcpConnection connection,
        SocketException exception,
        TcpCloseReason currentReason
    )
    {
        var method = typeof(TcpConnection).GetMethod(
            "MapSocketExceptionReason",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        return (TcpCloseReason)method.Invoke(connection, [exception, currentReason])!;
    }

    private static bool InvokeShouldReportReceiveFault(TcpCloseReason reason)
    {
        var method = typeof(TcpConnection).GetMethod(
            "ShouldReportReceiveFault",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;

        return (bool)method.Invoke(null, [reason])!;
    }

    private static async Task InvokeCancelConnectionAsync(TcpConnection connection)
    {
        var method = typeof(TcpConnection).GetMethod(
            "CancelConnectionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await (Task)method.Invoke(connection, [])!;
    }

    private static async Task<(Socket Client, Socket Server)> CreateConnectedSocketsAsync()
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);

        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connectTask = client.ConnectAsync((IPEndPoint)listener.LocalEndPoint!);
        var server = await listener.AcceptAsync();
        await connectTask;
        listener.Dispose();
        return (client, server);
    }

    private sealed class NoOpTcpHandler : ITcpConnectionHandler
    {
        public Task OnConnectedAsync(ITcpConnectionContext connection, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask<SequencePosition> OnReceivedAsync(
            ITcpConnectionContext connection,
            ReadOnlySequence<byte> buffer,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(buffer.End);

        public Task OnClosedAsync(
            ITcpConnectionContext connection,
            TcpCloseReason reason,
            Exception? error,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}
