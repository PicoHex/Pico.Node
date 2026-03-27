namespace Pico.Node;

internal sealed class TcpConnection : IAsyncDisposable
{
    private readonly TcpNode _node;
    private readonly Socket _socket;
    private readonly byte[] _receiveBuffer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly TcpConnectionContext _context;
    private int _closeState;

    public TcpConnection(TcpNode node, Socket socket, int receiveBufferSize)
    {
        _node = node;
        _socket = socket;
        _receiveBuffer = ArrayPool<byte>.Shared.Rent(receiveBufferSize);
        Id = Interlocked.Increment(ref _nextId);
        RemoteEndPoint = (IPEndPoint)socket.RemoteEndPoint!;
        ConnectedAtUtc = DateTimeOffset.UtcNow;
        _lastActivityTicks = ConnectedAtUtc.UtcTicks;
        _context = new TcpConnectionContext(this);
    }

    private static long _nextId;
    private long _lastActivityTicks;

    public long Id { get; }

    public IPEndPoint RemoteEndPoint { get; }

    public DateTimeOffset ConnectedAtUtc { get; }

    public DateTimeOffset LastActivityUtc => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);

    public TcpConnectionContext Context => _context;

    public async Task RunAsync(ITcpConnectionHandler handler, TimeSpan idleTimeout)
    {
        Exception? error = null;
        var reason = TcpCloseReason.RemoteClosed;

        try
        {
            await handler.OnConnectedAsync(_context, _cts.Token);

            while (!_cts.IsCancellationRequested)
            {
                if (IsIdle(idleTimeout))
                {
                    reason = TcpCloseReason.IdleTimeout;
                    break;
                }

                var bytesRead = await _socket.ReceiveAsync(
                    _receiveBuffer,
                    SocketFlags.None,
                    _cts.Token
                );
                if (bytesRead <= 0)
                {
                    reason = TcpCloseReason.RemoteClosed;
                    break;
                }

                Touch();
                await handler.OnReceivedAsync(_context, new ArraySegment<byte>(_receiveBuffer, 0, bytesRead), _cts.Token);
            }

            if (_cts.IsCancellationRequested && reason == TcpCloseReason.RemoteClosed)
            {
                reason = TcpCloseReason.LocalClose;
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            reason = TcpCloseReason.LocalClose;
        }
        catch (SocketException ex)
        {
            error = ex;
            reason = TcpCloseReason.ReceiveFault;
            _node.ReportFault(NodeFaultCode.ReceiveFailed, "tcp-receive", ex);
        }
        catch (Exception ex)
        {
            error = ex;
            reason = TcpCloseReason.HandlerFault;
            _node.ReportFault(NodeFaultCode.HandlerFailed, "tcp-handler", ex);
        }
        finally
        {
            await CloseCoreAsync(reason, error, handler);
        }
    }

    public async Task SendAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _closeState) != 0)
        {
            throw new InvalidOperationException("Connection is closed.");
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _closeState) != 0)
            {
                throw new InvalidOperationException("Connection is closed.");
            }

            await _socket.SendAsync(buffer, SocketFlags.None, cancellationToken);
            Touch();
        }
        catch (SocketException ex)
        {
            _node.ReportFault(NodeFaultCode.SendFailed, "tcp-send", ex);
            _ = CloseCoreAsync(TcpCloseReason.SendFault, ex, _node.Options.Handler);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Close()
    {
        _ = CloseCoreAsync(TcpCloseReason.LocalClose, null, _node.Options.Handler);
    }

    public void Close(TcpCloseReason reason)
    {
        _ = CloseCoreAsync(reason, null, _node.Options.Handler);
    }

    private bool IsIdle(TimeSpan idleTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            return false;
        }

        return DateTimeOffset.UtcNow - LastActivityUtc >= idleTimeout;
    }

    private void Touch()
    {
        Interlocked.Exchange(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
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

        _cts.Cancel();

        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
        }

        try
        {
            await handler.OnClosedAsync(_context, reason, error, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _node.ReportFault(NodeFaultCode.HandlerFailed, "tcp-close-handler", ex);
        }

        _node.OnConnectionClosed(this);
        await DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        _cts.Dispose();
        _socket.Dispose();
        ArrayPool<byte>.Shared.Return(_receiveBuffer);
        return ValueTask.CompletedTask;
    }
}
