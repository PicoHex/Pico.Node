namespace Pico.Node;

public sealed class TcpNode : INode, IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<long, TcpConnection> _connections = new();
    private readonly object _stateLock = new();
    private Task? _acceptTask;
    private volatile NodeState _state;
    private bool _disposed;

    public TcpNode(TcpNodeOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _listener = new Socket(options.Endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = options.NoDelay,
            LingerState = options.LingerState,
        };

        if (options.Endpoint.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _listener.DualMode = options.DualMode;
        }

        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _state = NodeState.Created;
    }

    internal TcpNodeOptions Options { get; }

    public EndPoint LocalEndPoint => _listener.LocalEndPoint ?? Options.Endpoint;

    public NodeState State => _state;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_stateLock)
        {
            if (_state is not (NodeState.Created or NodeState.Stopped))
            {
                throw new InvalidOperationException("Node is already running or starting.");
            }

            _state = NodeState.Starting;
        }

        try
        {
            _listener.Bind(Options.Endpoint);
            _listener.Listen(Options.Backlog);
            _acceptTask = Task.Run(AcceptLoopAsync, CancellationToken.None);
            _state = NodeState.Running;
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _state = NodeState.Stopped;
            ReportFault(NodeFaultCode.StartFailed, "tcp-start", ex);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_state is NodeState.Stopped or NodeState.Disposed or NodeState.Created)
        {
            return;
        }

        _state = NodeState.Stopping;
        _cts.Cancel();

        try
        {
            _listener.Close();
        }
        catch (Exception ex)
        {
            ReportFault(NodeFaultCode.StopFailed, "tcp-stop-close-listener", ex);
        }

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var connection in _connections.Values)
        {
            connection.Close(TcpCloseReason.NodeStopping);
        }

        while (_connections.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }

        _state = NodeState.Stopped;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var socket = await _listener.AcceptAsync(_cts.Token);
                socket.NoDelay = Options.NoDelay;
                socket.LingerState = Options.LingerState;

                if (_connections.Count >= Options.MaxConnections)
                {
                    ReportFault(NodeFaultCode.ConnectionRejected, "tcp-max-connections");
                    try
                    {
                        socket.Shutdown(SocketShutdown.Both);
                    }
                    catch
                    {
                    }

                    socket.Dispose();
                    continue;
                }

                var connection = new TcpConnection(this, socket, Options.ReceiveBufferSize);
                if (!_connections.TryAdd(connection.Id, connection))
                {
                    ReportFault(NodeFaultCode.ConnectionRejected, "tcp-track-connection");
                    await connection.DisposeAsync();
                    continue;
                }

                _ = Task.Run(
                    () => connection.RunAsync(Options.Handler, Options.IdleTimeout),
                    CancellationToken.None
                );
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex) when (_cts.IsCancellationRequested)
            {
                ReportFault(NodeFaultCode.AcceptFailed, "tcp-accept-cancelled", ex);
                break;
            }
            catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ReportFault(NodeFaultCode.AcceptFailed, "tcp-accept", ex);
                await Task.Delay(50, _cts.Token);
            }
        }
    }

    internal void OnConnectionClosed(TcpConnection connection)
    {
        _connections.TryRemove(connection.Id, out _);
    }

    internal void ReportFault(NodeFaultCode code, string operation, Exception? exception = null)
    {
        Options.FaultHandler?.Invoke(new NodeFault(code, operation, exception));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        _cts.Dispose();
        _listener.Dispose();
        _state = NodeState.Disposed;
        GC.SuppressFinalize(this);
    }
}
