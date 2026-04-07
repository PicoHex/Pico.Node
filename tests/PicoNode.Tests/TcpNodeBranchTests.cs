using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using PicoNode;
using PicoNode.Abs;

public sealed class TcpNodeBranchTests
{
    [Test]
    public async Task RejectAcceptedSocket_reports_fault_and_disposes_socket()
    {
        var faults = new ConcurrentQueue<NodeFault>();
        var node = CreateNode(faults.Enqueue);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        InvokeRejectAcceptedSocket(node, socket, NodeFaultCode.SessionRejected, "tcp.reject.limit");

        await Assert.That(faults.Count).IsEqualTo(1);
        await Assert.That(faults.TryPeek(out var fault)).IsTrue();
        await Assert.That(fault!.Code).IsEqualTo(NodeFaultCode.SessionRejected);
        await Assert.That(fault.Operation).IsEqualTo("tcp.reject.limit");
    }

    [Test]
    public async Task ReportFault_returns_when_handler_is_null()
    {
        var node = new TcpNode(
            new TcpNodeOptions
            {
                Endpoint = new IPEndPoint(IPAddress.Loopback, 0),
                ConnectionHandler = new NoOpTcpHandler(),
            }
        );

        InvokeReportFault(node, NodeFaultCode.ReceiveFailed, "tcp.receive", new InvalidOperationException("x"));
    }

    [Test]
    public async Task ReportFault_swallows_fault_handler_exceptions()
    {
        var calls = 0;
        var node = CreateNode(_ =>
        {
            calls++;
            throw new InvalidOperationException("fault handler failed");
        });

        InvokeReportFault(node, NodeFaultCode.SendFailed, "tcp.send", new SocketException((int)SocketError.NetworkDown));

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task TryTrackConnection_returns_false_when_node_is_stopping()
    {
        var pair = await CreateConnectedSocketsAsync();
        try
        {
            var node = CreateNode(_ => { });
            typeof(TcpNode)
                .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(node, NodeState.Stopping);

            var connection = new TcpConnection(node, pair.Server);
            var result = InvokeTryTrackConnection(node, connection);

            await Assert.That(result).IsFalse();
            await connection.DisposeAsync();
        }
        finally
        {
            pair.Client.Dispose();
            pair.Server.Dispose();
        }
    }

    private static TcpNode CreateNode(Action<NodeFault> faultHandler) =>
        new(
            new TcpNodeOptions
            {
                Endpoint = new IPEndPoint(IPAddress.Loopback, 0),
                ConnectionHandler = new NoOpTcpHandler(),
                FaultHandler = faultHandler,
            }
        );

    private static void InvokeRejectAcceptedSocket(
        TcpNode node,
        Socket socket,
        NodeFaultCode code,
        string operation
    )
    {
        var method = typeof(TcpNode).GetMethod(
            "RejectAcceptedSocket",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        method.Invoke(node, [socket, code, operation]);
    }

    private static void InvokeReportFault(
        TcpNode node,
        NodeFaultCode code,
        string operation,
        Exception? exception
    )
    {
        var method = typeof(TcpNode).GetMethod(
            "ReportFault",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        method.Invoke(node, [code, operation, exception]);
    }

    private static bool InvokeTryTrackConnection(TcpNode node, TcpConnection connection)
    {
        var method = typeof(TcpNode).GetMethod(
            "TryTrackConnection",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        return (bool)method.Invoke(node, [connection])!;
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
