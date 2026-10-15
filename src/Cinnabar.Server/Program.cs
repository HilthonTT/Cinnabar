using System.Runtime.InteropServices;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Hosting;

ServerSettings settings;
try
{
    settings = CommandLine.Parse(args, out bool showHelp);
    if (showHelp)
    {
        Console.WriteLine(CommandLine.Usage);
        return 0;
    }
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

using var shutdown = new CancellationTokenSource();

// Ctrl+C locally, SIGTERM from Docker/Kubernetes: both trigger a graceful stop (and final save).
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};
using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    shutdown.Cancel();
});

await using CinnabarServer server = CinnabarServer.Create(settings);
try
{
    await server.RunAsync(shutdown.Token);
    return 0;
}
catch (Exception e) when (e is InvalidDataException or System.Net.Sockets.SocketException or ArgumentException)
{
    Console.Error.WriteLine($"Fatal: {e.Message}");
    return 1;
}
