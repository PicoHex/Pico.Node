namespace PicoNode.Abs;

public interface INode
{
    EndPoint LocalEndPoint { get; }
    NodeState State { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
