namespace PicoNode;

internal static class NodeHelper
{
    internal static void ReportFault(
        Action<NodeFault>? faultHandler,
        NodeFaultCode code,
        string operation,
        Exception? exception = null
    )
    {
        faultHandler?.Invoke(new NodeFault(code, operation, exception));
    }
}
