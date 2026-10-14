using System.Globalization;
using System.Net;

namespace FaliNamIgrac;

/// <summary>Shared route adapters supply synchronous UI commits. Ratings must resolve eligibility
/// through the ratings module; without that adapter ratings fail closed.</summary>
public sealed class AuthenticatedNotificationResolver(
    IChatService chats,
    IGamesApi games,
    Func<int?> currentUser,
    Action<ChatRoom> openChat,
    Action<GameDetails> openGame,
    Func<int, string, CancellationToken, Task<Action?>>? resolveRating = null
) : INotificationDestinationResolver
{
    public async Task<Action?> ResolveAsync(
        int userId,
        NotificationTarget target,
        CancellationToken token
    )
    {
        if (currentUser() != userId)
            return null;
        try
        {
            Action? result;
            switch (target.Destination)
            {
                case NotificationDestination.Chat:
                    var user = userId.ToString(CultureInfo.InvariantCulture);
                    var room = await chats.GetAsync(target.Id, user, token);
                    result =
                        room is null || room.Id != target.Id || !room.Participants.Contains(user)
                            ? null
                            : () => openChat(room);
                    break;
                case NotificationDestination.GameDetails:
                    var game = await games.DetailsAsync(target.Id, token);
                    result = game.Game.Id == target.Id ? () => openGame(game) : null;
                    break;
                case NotificationDestination.RatePlayers:
                    result = resolveRating is null
                        ? null
                        : await resolveRating(userId, target.Id, token);
                    break;
                default:
                    return null;
            }
            token.ThrowIfCancellationRequested();
            return result is null
                ? null
                : () =>
                {
                    if (currentUser() == userId)
                        result();
                };
        }
        catch (HttpRequestException error)
            when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
