using System.Net.NetworkInformation;

namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Production <see cref="IPortProbe"/>. Uses .NET's built-in
/// <see cref="IPGlobalProperties"/> to read the OS's active-listener
/// table. Cross-platform — no shell-out, no extra dependencies.
/// </summary>
public sealed class PortProbe : IPortProbe
{
    public bool IsListening(int port)
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            return listeners.Any(ep => ep.Port == port);
        }
        catch
        {
            // Fail-safe: if the probe itself fails, report not-listening
            // so the rest of the pre-flight can continue. The downstream
            // bind attempt will still error if the port is actually held.
            return false;
        }
    }
}
