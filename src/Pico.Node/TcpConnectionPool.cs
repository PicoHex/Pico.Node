namespace Pico.Node;

internal sealed class TcpConnectionPool
{
    private readonly ConcurrentBag<TcpConnection> _pool = new();
    private readonly TcpNode _node;

    public TcpConnectionPool(TcpNode node)
    {
        _node = node;
    }

    public TcpConnection Rent(Socket socket)
    {
        if (!_pool.TryTake(out var connection))
        {
            connection = new TcpConnection();
        }

        connection.Initialize(_node, this, socket);
        return connection;
    }

    public void Return(TcpConnection connection)
    {
        _pool.Add(connection);
    }
}
