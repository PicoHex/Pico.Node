namespace Pico.Node;

internal sealed class SocketIoEventArgs : SocketAsyncEventArgs
{
    private TaskCompletionSource<SocketAsyncEventArgs>? _completion;

    public SocketIoEventArgs()
    {
        Completed += OnCompleted;
    }

    public Task<SocketAsyncEventArgs> ReceiveAsync(Socket socket)
        => ExecuteAsync(socket.ReceiveAsync);

    public Task<SocketAsyncEventArgs> SendAsync(Socket socket)
        => ExecuteAsync(socket.SendAsync);

    public Task<SocketAsyncEventArgs> AcceptAsync(Socket socket)
        => ExecuteAsync(socket.AcceptAsync);

    public void Reset()
    {
        AcceptSocket = null;
        DisconnectReuseSocket = false;
        RemoteEndPoint = null;
        UserToken = null;
        SetBuffer(null, 0, 0);
    }

    private Task<SocketAsyncEventArgs> ExecuteAsync(Func<SocketAsyncEventArgs, bool> start)
    {
        var completion = new TaskCompletionSource<SocketAsyncEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        if (Interlocked.CompareExchange(ref _completion, completion, null) is not null)
        {
            throw new InvalidOperationException("Socket async operation is already pending.");
        }

        try
        {
            if (!start(this))
            {
                Complete(this);
            }
        }
        catch
        {
            Interlocked.Exchange(ref _completion, null);
            throw;
        }

        return completion.Task;
    }

    private void OnCompleted(object? sender, SocketAsyncEventArgs eventArgs)
    {
        Complete(eventArgs);
    }

    private void Complete(SocketAsyncEventArgs eventArgs)
    {
        Interlocked.Exchange(ref _completion, null)?.TrySetResult(eventArgs);
    }
}
