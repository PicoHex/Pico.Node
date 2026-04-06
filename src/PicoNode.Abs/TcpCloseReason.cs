namespace PicoNode.Abs;

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
