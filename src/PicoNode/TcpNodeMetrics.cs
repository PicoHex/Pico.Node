namespace PicoNode;

public readonly record struct TcpNodeMetrics(
    long TotalAccepted,
    long TotalRejected,
    long TotalClosed,
    int ActiveConnections,
    long TotalBytesSent,
    long TotalBytesReceived
);
