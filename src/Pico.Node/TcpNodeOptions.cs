namespace Pico.Node;

public sealed class TcpNodeOptions
{
    public required IPEndPoint Endpoint { get; init; }
    public required ITcpConnectionHandler Handler { get; init; }
    public Action<NodeFault>? FaultHandler { get; init; }
    public int MaxConnections { get; init; } = 1000;
    public int ReceiveBufferSize { get; init; } = 4096;
    public bool NoDelay { get; init; } = true;
    public LingerOption LingerState { get; init; } = new(false, 0);
    public int Backlog { get; init; } = 128;
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public bool DualMode { get; init; }
}
