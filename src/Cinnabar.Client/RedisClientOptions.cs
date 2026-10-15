namespace Cinnabar.Client;

/// <summary>Connection settings for <see cref="RedisClient"/>.</summary>
public sealed class RedisClientOptions
{
    /// <summary>Server host name or IP address.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Server port.</summary>
    public int Port { get; set; } = 6379;

    /// <summary>Maximum number of pooled connections, i.e. concurrent commands.</summary>
    public int PoolSize { get; set; } = 10;

    /// <summary>How long a command waits for a free pooled connection before failing.</summary>
    public TimeSpan PoolTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for a TCP connection to be established.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Password; when set, every new connection sends <c>AUTH</c>.</summary>
    public string? Password { get; set; }

    /// <summary>ACL username sent with the password. Defaults to <c>default</c>.</summary>
    public string Username { get; set; } = "default";

    /// <summary>
    /// Sets the options from a connection string:
    /// <c>host:port</c>, <c>password@host:port</c>, <c>user:password@host:port</c>, optionally
    /// prefixed with <c>redis://</c>. Reading it back masks the password.
    /// </summary>
    public string? ConnectionString
    {
        get => Password is null ? $"{Host}:{Port}" : $"{Username}:***@{Host}:{Port}";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string remaining = value.Trim();
            int scheme = remaining.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                remaining = remaining[(scheme + 3)..];
            }

            // A connection string describes the whole endpoint: forget earlier credentials.
            Username = "default";
            Password = null;

            // Passwords may contain '@', so split on the last one.
            int at = remaining.LastIndexOf('@');
            if (at >= 0)
            {
                string credentials = remaining[..at];
                remaining = remaining[(at + 1)..];

                int colon = credentials.IndexOf(':');
                if (colon >= 0)
                {
                    Username = colon == 0 ? "default" : credentials[..colon];
                    Password = credentials[(colon + 1)..];
                }
                else
                {
                    Password = credentials;
                }
            }

            // Drop a database path such as "/0" (Cinnabar has a single database).
            int slash = remaining.IndexOf('/');
            if (slash >= 0)
            {
                remaining = remaining[..slash];
            }
            int portSeparator = remaining.LastIndexOf(':');
            if (portSeparator > 0 && int.TryParse(remaining[(portSeparator + 1)..], out int port))
            {
                Host = remaining[..portSeparator];
                Port = port;
            }
            else
            {
                Host = remaining;
            }
        }
    }

    /// <summary>Checks the options for obvious mistakes.</summary>
    /// <exception cref="ArgumentException">An option is out of range.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Host must be set.", nameof(Host));
        }

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentException("Port must be between 1 and 65535.", nameof(Port));
        }

        if (PoolSize < 1)
        {
            throw new ArgumentException("PoolSize must be at least 1.", nameof(PoolSize));
        }
    }
}
