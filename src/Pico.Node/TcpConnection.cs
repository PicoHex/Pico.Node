namespace Pico.Node;

internal sealed class TcpConnection : IAsyncDisposable
{
    private TcpNode? _node;
    private TcpConnectionPool? _pool;
    private Socket? _socket;
    private SocketIoEventArgs? _receiveArgs;
    private SocketIoEventArgs? _sendArgs;
    private SemaphoreSlim? _sendLock;
    private CancellationTokenSource? _cts;
    private TcpConnectionContext? _context;
    private int _closeState;
    private int _disposeState;
    private int _generation;
    private IPEndPoint? _remoteEndPoint;
    private DateTimeOffset _connectedAtUtc;

    public void Initialize(TcpNode node, TcpConnectionPool pool, Socket socket)
    {
        _node = node;
        _pool = pool;
        _socket = socket;
        _receiveArgs = node.EventArgsPool.RentReceiveArgs();
        _sendArgs = node.EventArgsPool.RentSendArgs();
        _sendLock = new SemaphoreSlim(1, 1);
        _cts = new CancellationTokenSource();
        Id = Interlocked.Increment(ref _nextId);
        _remoteEndPoint = (IPEndPoint)socket.RemoteEndPoint!;
        _connectedAtUtc = DateTimeOffset.UtcNow;
        _lastActivityTicks = _connectedAtUtc.UtcTicks;
        _generation++;
        _closeState = 0;
        _disposeState = 0;
        _context = new TcpConnectionContext(this, _generation, Id, _remoteEndPoint, _connectedAtUtc);
    }

    private static long _nextId;
    private long _lastActivityTicks;

    public long Id { get; private set; }

    public IPEndPoint RemoteEndPoint => _remoteEndPoint!;

    public DateTimeOffset ConnectedAtUtc => _connectedAtUtc;

    public DateTimeOffset LastActivityUtc => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);

    public TcpConnectionContext Context => _context ?? throw new InvalidOperationException("Connection is not initialized.");

    public async Task RunAsync(ITcpConnectionHandler handler)
    {
        Exception? error = null;
        var reason = TcpCloseReason.RemoteClosed;

        try
        {
            var context = Context;
            var cts = _cts ?? throw new InvalidOperationException("Connection token source is missing.");
            var ct = cts.Token;
            await handler.OnConnectedAsync(context, ct);

            while (!cts.IsCancellationRequested)
            {
                var receiveArgs = _receiveArgs ?? throw new InvalidOperationException("Receive args missing.");
                var socket = _socket ?? throw new InvalidOperationException("Socket missing.");
                var receiveResult = await receiveArgs.ReceiveAsync(socket);
                if (receiveResult.SocketError != SocketError.Success)
                {
                    if (
                        receiveResult.SocketError
                        is SocketError.ConnectionReset
                            or SocketError.ConnectionAborted
                            or SocketError.OperationAborted
                    )
                    {
                        reason = cts.IsCancellationRequested
                            ? TcpCloseReason.LocalClose
                            : TcpCloseReason.RemoteClosed;
                        break;
                    }

                    throw new SocketException((int)receiveResult.SocketError);
                }

                var bytesRead = receiveResult.BytesTransferred;
                var receiveBuffer = receiveArgs.Buffer ?? throw new InvalidOperationException("Receive buffer is not available.");
                if (bytesRead <= 0)
                {
                    reason = TcpCloseReason.RemoteClosed;
                    break;
                }

                Touch();
                var receiveTask = handler.OnReceivedAsync(
                    context,
                    new ArraySegment<byte>(receiveBuffer, 0, bytesRead),
                    ct
                );
                if (!receiveTask.IsCompletedSuccessfully)
                {
                    await receiveTask;
                }
            }

            if (cts.IsCancellationRequested && reason == TcpCloseReason.RemoteClosed)
            {
                reason = TcpCloseReason.LocalClose;
            }
        }
        catch (OperationCanceledException) when ((_cts?.IsCancellationRequested).GetValueOrDefault())
        {
            reason = TcpCloseReason.LocalClose;
        }
        catch (ObjectDisposedException) when ((_cts?.IsCancellationRequested).GetValueOrDefault() || Volatile.Read(ref _closeState) != 0)
        {
            reason = TcpCloseReason.LocalClose;
        }
        catch (SocketException ex)
        {
            error = ex;
            reason = TcpCloseReason.ReceiveFault;
            (_node ?? throw new InvalidOperationException("Connection node is missing.")).ReportFault(
                NodeFaultCode.ReceiveFailed,
                "tcp-receive",
                ex
            );
        }
        catch (Exception ex)
        {
            error = ex;
            reason = TcpCloseReason.HandlerFault;
            (_node ?? throw new InvalidOperationException("Connection node is missing.")).ReportFault(
                NodeFaultCode.HandlerFailed,
                "tcp-handler",
                ex
            );
        }
        finally
        {
            await CloseCoreAsync(reason, error, handler);
        }
    }

    public Task SendAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default)
        => SendAsync(_generation, buffer, cancellationToken);

    public async Task SendAsync(
        int generation,
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        EnsureGeneration(generation);

        if (Volatile.Read(ref _closeState) != 0)
        {
            throw new InvalidOperationException("Connection is closed.");
        }

        var sendLock = _sendLock ?? throw new InvalidOperationException("Send lock missing.");
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            EnsureGeneration(generation);

            if (Volatile.Read(ref _closeState) != 0)
            {
                throw new InvalidOperationException("Connection is closed.");
            }

            if (buffer.Count == 0)
            {
                return;
            }

            var remaining = buffer;
            while (remaining.Count > 0)
            {
                var sendArgs = _sendArgs ?? throw new InvalidOperationException("Send args missing.");
                var socket = _socket ?? throw new InvalidOperationException("Socket missing.");
                sendArgs.SetBuffer(remaining.Array, remaining.Offset, remaining.Count);
                var sendResult = await sendArgs.SendAsync(socket);
                if (sendResult.SocketError != SocketError.Success)
                {
                    throw new SocketException((int)sendResult.SocketError);
                }

                if (sendResult.BytesTransferred <= 0)
                {
                    throw new SocketException((int)SocketError.ConnectionAborted);
                }

                if (sendResult.BytesTransferred == remaining.Count)
                {
                    break;
                }

                remaining = new ArraySegment<byte>(
                    remaining.Array!,
                    remaining.Offset + sendResult.BytesTransferred,
                    remaining.Count - sendResult.BytesTransferred
                );
            }

            Touch();
        }
        catch (SocketException ex)
        {
            var node = _node ?? throw new InvalidOperationException("Connection node is missing.");
            node.ReportFault(NodeFaultCode.SendFailed, "tcp-send", ex);
            _ = CloseCoreAsync(TcpCloseReason.SendFault, ex, node.Options.Handler);
            throw;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _closeState) != 0)
        {
            throw new InvalidOperationException("Connection is closed.");
        }
        finally
        {
            sendLock.Release();
        }
    }

    public void Close()
    {
        var node = _node ?? throw new InvalidOperationException("Connection node is missing.");
        _ = CloseCoreAsync(TcpCloseReason.LocalClose, null, node.Options.Handler);
    }

    public void Close(int generation)
    {
        EnsureGeneration(generation);
        Close();
    }

    public void Close(TcpCloseReason reason)
    {
        var node = _node ?? throw new InvalidOperationException("Connection node is missing.");
        _ = CloseCoreAsync(reason, null, node.Options.Handler);
    }

    internal bool IsIdle(TimeSpan idleTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            return false;
        }

        return DateTimeOffset.UtcNow - LastActivityUtc >= idleTimeout;
    }

    internal DateTimeOffset GetLastActivityUtc(int generation, DateTimeOffset fallback)
        => generation == _generation ? LastActivityUtc : fallback;

    private void Touch()
    {
        Interlocked.Exchange(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    private void EnsureGeneration(int generation)
    {
        if (generation != _generation || Volatile.Read(ref _closeState) != 0)
        {
            throw new InvalidOperationException("Connection is closed.");
        }
    }

    private async Task CloseCoreAsync(
        TcpCloseReason reason,
        Exception? error,
        ITcpConnectionHandler handler
    )
    {
        if (Interlocked.Exchange(ref _closeState, 1) != 0)
        {
            return;
        }

        var cts = _cts ?? throw new InvalidOperationException("Connection token source is missing.");
        var node = _node ?? throw new InvalidOperationException("Connection node is missing.");
        var socket = _socket ?? throw new InvalidOperationException("Socket missing.");
        var context = Context;

        cts.Cancel();
        node.OnConnectionClosed(this);

        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
        }

        try
        {
            var closeTask = handler.OnClosedAsync(context, reason, error, CancellationToken.None);
            if (!closeTask.IsCompletedSuccessfully)
            {
                await closeTask;
            }
        }
        catch (Exception ex)
        {
            node.ReportFault(NodeFaultCode.HandlerFailed, "tcp-close-handler", ex);
        }

        await DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        var node = _node;
        var pool = _pool;
        var receiveArgs = _receiveArgs;
        var sendArgs = _sendArgs;
        var sendLock = _sendLock;
        var cts = _cts;
        var socket = _socket;

        _receiveArgs = null;
        _sendArgs = null;
        _sendLock = null;
        _cts = null;
        _socket = null;
        _context = null;
        _remoteEndPoint = null;

        if (node is not null && receiveArgs is not null)
        {
            node.EventArgsPool.Return(receiveArgs);
        }

        if (node is not null && sendArgs is not null)
        {
            node.EventArgsPool.Return(sendArgs);
        }

        sendLock?.Dispose();
        cts?.Dispose();
        socket?.Dispose();

        _node = null;
        _pool = null;
        pool?.Return(this);
        return ValueTask.CompletedTask;
    }
}
