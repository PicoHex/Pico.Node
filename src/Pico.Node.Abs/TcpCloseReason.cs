namespace Pico.Node.Abs;

public enum TcpCloseReason
{
    LocalClose,
    RemoteClosed,
    IdleTimeout,
    HandlerFault,
    ReceiveFault,
    SendFault,
    NodeStopping,
    Rejected,
}
