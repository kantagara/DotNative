using System.Net.Http;
using System.Net.Sockets;

namespace FaliNamIgrac;

internal static class FniErrors
{
    public static bool IsConnectionFailure(Exception error)
    {
        var pending = new Stack<Exception>();
        pending.Push(error);
        while (pending.TryPop(out var current))
        {
            if (
                current
                is HttpRequestException
                    or SocketException
                    or TimeoutException
                    or TaskCanceledException
            )
                return true;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    pending.Push(inner);
            }
            else if (current.InnerException is { } inner)
                pending.Push(inner);
        }

        return false;
    }

    public static string UserMessage(AppLocalizations text, Exception error) =>
        IsConnectionFailure(error) ? text.ConnectionFailure : text.RequestFailed;
}
