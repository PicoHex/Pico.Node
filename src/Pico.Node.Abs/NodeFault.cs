namespace Pico.Node.Abs;

public readonly struct NodeFault
{
    public NodeFault(NodeFaultCode code, string operation, Exception? exception = null)
    {
        Code = code;
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        Exception = exception;
    }

    public NodeFaultCode Code { get; }

    public string Operation { get; }

    public Exception? Exception { get; }
}
