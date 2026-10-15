using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands;

/// <summary>
/// Executes one command. Implementations are stateless singletons; decorate them with
/// <see cref="CommandAttribute"/> and they are registered automatically.
/// </summary>
/// <remarks>
/// The dispatcher has already checked authentication, argument count, subscribed mode and
/// transaction queueing. Throwing a <see cref="Cinnabar.Storage.StorageException"/> turns its
/// message into an error reply.
/// </remarks>
internal interface ICommandHandler
{
    /// <summary>Runs the command for <paramref name="session"/>.</summary>
    ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken);
}
