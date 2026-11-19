namespace Websiter;

/// <summary>Base exception for Websiter framework errors.</summary>
public class WebsiterException : Exception
{
    public WebsiterException(string message) : base(message) { }
    public WebsiterException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when configuration is invalid or missing required values.</summary>
public class WebsiterConfigurationException : WebsiterException
{
    public WebsiterConfigurationException(string message) : base(message) { }
}

/// <summary>Thrown when a worker fails to start or communicate.</summary>
public class WorkerException : WebsiterException
{
    public WorkerException(string message) : base(message) { }
    public WorkerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when an IPC operation fails (timeout, unreachable worker, protocol error).</summary>
public class IpcException : WebsiterException
{
    public IpcException(string message) : base(message) { }
    public IpcException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when a component cannot be resolved or rendered.</summary>
public class ComponentException : WebsiterException
{
    public ComponentException(string message) : base(message) { }
}

/// <summary>Thrown when a route cannot be matched or a request is invalid.</summary>
public class RoutingException : WebsiterException
{
    public RoutingException(string message) : base(message) { }
}
