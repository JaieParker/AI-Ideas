namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Probes whether a TCP port is currently held by a listener on
/// the local machine. Used by skill pre-flight checks to detect
/// port conflicts (e.g. another OTLP receiver already bound to
/// :4318) and tell the user how to recover (BR-OTEL-005).
///
/// The probe is read-only — it never opens, binds, or closes
/// listeners itself. It only inspects the OS's active-listener
/// table.
/// </summary>
public interface IPortProbe
{
    /// <summary>
    /// True if some process is listening on <paramref name="port"/>
    /// at any local address (loopback or otherwise). False if the
    /// port is free.
    /// </summary>
    bool IsListening(int port);
}
