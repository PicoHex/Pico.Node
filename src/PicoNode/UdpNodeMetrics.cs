namespace PicoNode;

public readonly record struct UdpNodeMetrics(
    long TotalDatagramsReceived,
    long TotalDatagramsSent,
    long TotalBytesReceived,
    long TotalBytesSent,
    long TotalDropped
);
