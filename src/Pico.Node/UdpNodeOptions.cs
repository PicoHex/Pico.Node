namespace Pico.Node;

public sealed class UdpNodeOptions
{
    public required IPEndPoint Endpoint { get; init; }
    public required IUdpDatagramHandler Handler { get; init; }
    public Action<NodeFault>? FaultHandler { get; init; }
    public int ReceiveBufferSize { get; init; } = 1 << 20;
    public int SendBufferSize { get; init; } = 1 << 20;
    public int WorkerCount { get; init; } = 1;
    public int QueueCapacityPerWorker { get; init; } = 1024;
    public bool EnableBroadcast { get; init; } = true;
    public UdpOverflowMode OverflowMode { get; init; } = UdpOverflowMode.DropNewest;
}
