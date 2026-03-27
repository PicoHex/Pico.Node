namespace Pico.Node.Abs;

public enum NodeFaultCode
{
    StartFailed,
    StopFailed,
    AcceptFailed,
    ConnectionRejected,
    ReceiveFailed,
    SendFailed,
    HandlerFailed,
    UdpReceiveFailed,
    UdpDatagramDropped,
    UdpHandlerFailed,
}
