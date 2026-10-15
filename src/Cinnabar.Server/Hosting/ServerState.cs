using System.Reflection;

namespace Cinnabar.Server.Hosting;

/// <summary>Runtime facts about the running server, reported by INFO.</summary>
internal sealed class ServerState(TimeProvider timeProvider)
{
    /// <summary>When the server object was created.</summary>
    public DateTimeOffset StartedAt { get; } = timeProvider.GetUtcNow();

    /// <summary>The port actually bound (differs from the setting when it was 0).</summary>
    public int Port { get; set; }

    /// <summary>The Cinnabar version.</summary>
    public string Version { get; } =
        typeof(ServerState).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    /// <summary>Time since start.</summary>
    public TimeSpan Uptime => timeProvider.GetUtcNow() - StartedAt;
}
