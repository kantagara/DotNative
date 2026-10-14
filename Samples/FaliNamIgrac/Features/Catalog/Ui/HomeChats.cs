using DotNative;

namespace FaliNamIgrac;

public sealed partial class HomeScreen
{
    private readonly ChatNotifier chat;
    private readonly ListView<ChatMessage> chatMessages;
    private readonly ListView<ChatRoom> chatRooms;
    private CancellationTokenSource? chatRefresh;
    private readonly State<bool> showChatCreateMenu = new(false);
    private readonly State<bool> showChatInfo = new(false);
    private readonly State<bool> confirmDeleteChat = new(false);
    private readonly State<bool> confirmLeave = new(false);

    private Element ChatPage((float W, float H, float R) scale, float top)
    {
        var view = chat.State;
        if (view.Searching && view.CreatingGroup)
            return ChatGroupEditor(scale)
                .Padding(16 * scale.W, top, 16 * scale.W, 16 * scale.H)
                .Grow();
        return new VStack(
            view.Room is null
                ? new VStack(
                    new HStack(
                        new Text(Loc.Chats)
                            .TextStyle(TextStyleFor("Bold"))
                            .FontSize(32 * scale.W)
                            .ForegroundColor(Ink)
                            .Grow(),
                        ChatIcon(
                            "+",
                            () => showChatCreateMenu.Value = !showChatCreateMenu.Value,
                            scale.R
                        )
                    ).CrossAlignment(CrossAxisAlignment.Center),
                    Gap(36 * scale.H)
                )
                : new VStack(),
            Chats(scale)
        )
            .Padding(
                16 * scale.W,
                top + (view.Room is null ? 16 * scale.H : 0),
                16 * scale.W,
                16 * scale.H
                    + (view.Room is null ? 0 : Math.Max(0, Insets.Keyboard - Insets.Bottom))
            )
            .Grow();
    }

    private Element Chats((float W, float H, float R) scale)
    {
        var view = chat.State;
        var r = scale.R;
        var userId = (session.State as SessionState.SignedIn)?.Profile.Id ?? "";
        if (view.Room is { } room)
        {
            if (showChatInfo.Value)
                return ChatInfo(room, userId, r);
            var group = !string.IsNullOrEmpty(room.Admin);
            return new VStack(new HStack(
                    ChatIcon(
                        "\ue092",
                        () =>
                        {
                            chat.Back();
                            _ = chat.RefreshAsync(lifetime.Token);
                        },
                        r
                    ),
                    group
                        ? new VStack()
                        : Avatar(RoomImage(room, userId), RoomName(room, userId), 40 * r),
                    new Text(RoomName(room, userId))
                        .TextStyle(TextStyleFor("Medium"))
                        .FontSize(22 * scale.W)
                        .ForegroundColor(Ink)
                        .MaxLines(1)
                        .TextOverflow(TextOverflow.EllipsisEnd)
                        .Grow()
                        .Shrink()
                        .FlexBasis(0)
                        .MinWidth(0),
                    group
                        ? ChatIcon(
                            "\uf00d",
                            () =>
                            {
                                confirmDeleteChat.Value = false;
                                showChatInfo.Value = true;
                            },
                            r
                        )
                        : new VStack()
                ).Spacing(12 * scale.W).Height(56 * scale.H).CrossAlignment(CrossAxisAlignment.Center), chatMessages, ChatError(r), new HStack(
                    new TextField(view.Draft, chat.SetDraft)
                    {
                        Placeholder = Loc.ChatMessageHint,
                        Input = new()
                        {
                            IsMultiline = true,
                            Capitalization = TextCapitalization.Sentences,
                        },
                    }
                        .TextStyle(TextStyleFor())
                        .FontSize(16 * scale.W)
                        .ForegroundColor(Ink)
                        .BackgroundColor(Card(false))
                        .Padding(12 * r)
                        .CornerRadius(16 * r)
                        .Grow()
                        .Height(52 * r),
                    view.Sending
                        ? new VStack(BusyIndicator(Accent))
                            .Width(48 * r)
                            .Height(52 * r)
                            .MainAlignment(MainAxisAlignment.Center)
                            .CrossAlignment(CrossAxisAlignment.Center)
                        : new Button("\ue571", () => _ = chat.SendAsync(lifetime.Token))
                            .TextAlignment(TextAlignment.Center)
                            .TextStyle(TextStyleFor("Icons"))
                            .FontSize(24 * r)
                            .ForegroundColor(Ink)
                            .Padding(12 * r)
                            .Width(48 * r)
                            .Height(52 * r)
                            .Enabled(
                                !view.Sending
                                    && (
                                        !string.IsNullOrWhiteSpace(view.Draft)
                                        || chat.HasPendingMessage
                                    )
                            )
                            .Opacity(view.Sending ? 0.4f : 1)
                ).CrossAlignment(CrossAxisAlignment.Center).Spacing(8 * r).Padding(12 * scale.W, 8 * scale.H).BackgroundColor(Card(false)).CornerRadius(24 * r)).Spacing(12 * r).Grow().Shrink().FlexBasis(0).MinHeight(0);
        }
        return new VStack([new HStack(
                new TextField(view.Searching ? "" : view.Search, chat.SetSearch)
                {
                    Placeholder = Loc.Search,
                }
                    .TextStyle(TextStyleFor())
                    .FontSize(16 * r)
                    .ForegroundColor(Ink)
                    .Padding(16 * r, 0)
                    .Grow()
                    .Shrink()
                    .FlexBasis(0)
                    .MinWidth(0)
                    .Height(48 * scale.H),
                new VStack(
                    new Text("\ue567")
                        .TextAlignment(TextAlignment.Center)
                        .TextStyle(TextStyleFor("Icons"))
                        .FontSize(24 * r)
                        .ForegroundColor(Color.White)
                )
                    .Width(48 * scale.W)
                    .Height(48 * scale.H)
                    .CornerRadius(14 * r)
                    .BackgroundColor(new Color(38, 38, 38))
                    .MainAlignment(MainAxisAlignment.Center)
                    .CrossAlignment(CrossAxisAlignment.Center)
            ).Padding(2 * r).BackgroundColor(theme.Mode == ThemeMode.Dark ? new Color(37, 37, 37) : new Color(233, 233, 233)).CornerRadius(16 * r), Gap(36 * scale.H), ChatError(r), view.Rooms is { Count: 0 } ? Centered(Loc.NoChatsAvailable, r) : chatRooms]).Grow().Shrink().FlexBasis(0).MinHeight(0);
    }

    private Element ChatInfo(ChatRoom room, string userId, float r)
    {
        var admin = room.Admin == userId;
        return new VStack(
            new HStack(
                ChatIcon("\ue092", () => showChatInfo.Value = false, r),
                new VStack().Grow(),
                admin
                    ? ChatIcon(
                        "\ue494",
                        () =>
                        {
                            showChatInfo.Value = false;
                            chat.EditGroup();
                        },
                        r
                    )
                    : new Button(Loc.LeaveGroup, () => confirmLeave.Value = true)
                        .TextStyle(TextStyleFor("Medium"))
                        .FontSize(14 * r)
                        .ForegroundColor(Accent)
                        .Height(48 * r),
                admin ? ChatIcon("\ue1ba", () => confirmDeleteChat.Value = true, r) : new VStack()
            ).CrossAlignment(CrossAxisAlignment.Center),
            new HStack(
                Avatar(room.ImageUrl, RoomName(room, userId), 80 * r),
                new Text(RoomName(room, userId))
                    .TextStyle(TextStyleFor("Bold"))
                    .FontSize(32 * r)
                    .ForegroundColor(Ink)
                    .MaxLines(1)
                    .TextOverflow(TextOverflow.EllipsisEnd)
                    .Grow()
                    .Shrink()
                    .FlexBasis(0),
                admin
                    ? ChatIcon(
                        "\uf00d",
                        () =>
                        {
                            chat.SetGroupName(room.Title ?? "");
                            renameChat.Value = true;
                        },
                        r
                    )
                    : new VStack()
            ).Spacing(16 * r).CrossAlignment(CrossAxisAlignment.Center),
            Gap(36 * r),
            new ScrollView(
                new VStack(
                    room.Users.Select(user =>
                            (Element)
                                new VStack(
                                    new HStack(
                                        Avatar(user.ProfileImageUrl, user.Name, 40 * r),
                                        new VStack(
                                            new Text(user.Name)
                                                .TextStyle(TextStyleFor("Medium"))
                                                .FontSize(16 * r)
                                                .ForegroundColor(Ink),
                                            new Text(user.Nickname ?? "")
                                                .TextStyle(TextStyleFor())
                                                .FontSize(14 * r)
                                                .ForegroundColor(AuthHint)
                                        )
                                            .Grow()
                                            .Shrink()
                                            .FlexBasis(0),
                                        admin && user.Id != userId
                                            ? new Button(
                                                Loc.Remove,
                                                () =>
                                                    _ = chat.RemoveMemberAsync(
                                                        user.Id,
                                                        lifetime.Token
                                                    )
                                            )
                                                .TextStyle(TextStyleFor("Medium"))
                                                .FontSize(14 * r)
                                                .ForegroundColor(Accent)
                                                .Enabled(!chat.State.Loading)
                                                .Height(48 * r)
                                            : new VStack()
                                    )
                                        .Spacing(16 * r)
                                        .CrossAlignment(CrossAxisAlignment.Center)
                                        .Padding(0, 8 * r),
                                    new VStack().Height(1).BackgroundColor(theme.Surface)
                                )
                        )
                        .ToArray()
                )
            )
                .Grow()
                .Shrink()
                .FlexBasis(0)
                .MinHeight(0),
            ChatError(r)
        ).Grow().Shrink().FlexBasis(0).MinHeight(0);
    }

    private Node ChatRoomRow(ChatRoom room)
    {
        _ = ui.Themes.Current.Value;
        var r = Scale().R;
        var user = (session.State as SessionState.SignedIn)?.Profile.Id ?? "";
        var name = RoomName(room, user);
        return new VStack(
            new HStack(
                Avatar(RoomImage(room, user), name, 40 * r),
                new VStack(
                    new HStack(
                        new Text(name)
                            .TextStyle(TextStyleFor("Bold"))
                            .FontSize(16 * r)
                            .ForegroundColor(Ink)
                            .MaxLines(1)
                            .TextOverflow(TextOverflow.EllipsisEnd)
                            .Grow(),
                        new Text(room.LastMessageAt?.ToLocalTime().ToString("dd.MM HH:mm") ?? "")
                            .TextStyle(TextStyleFor())
                            .FontSize(12 * r)
                            .ForegroundColor(theme.MutedForeground)
                    ).Spacing(8 * r),
                    new Text(room.LastMessageContent ?? Loc.ChatNewConversation)
                        .TextStyle(TextStyleFor())
                        .FontSize(14 * r)
                        .ForegroundColor(theme.MutedForeground)
                        .MaxLines(1)
                        .TextOverflow(TextOverflow.EllipsisEnd)
                )
                    .Spacing(4 * r)
                    .Grow()
                    .Shrink()
                    .FlexBasis(0)
                    .MinWidth(0)
            )
                .Spacing(12 * r)
                .Padding(0, 12 * r)
                .CrossAlignment(CrossAxisAlignment.Center),
            new Button("", () => _ = chat.OpenAsync(room, lifetime.Token))
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
                .WidthPercent(100)
                .HeightPercent(100)
        ).Margin(0, 0, 0, 4 * r);
    }

    private string RoomName(ChatRoom room, string userId) =>
        string.IsNullOrWhiteSpace(room.DisplayName(userId))
            ? Loc.ChatConversation
            : room.DisplayName(userId);

    private Element ChatError(float r)
    {
        var state = chat.State;
        var text =
            state.Loading && (state.Room is null ? state.Rooms is null : state.Messages is null)
                ? Loc.Loading
                : string.Join(
                    "\n",
                    new[] { state.Error, state.CacheError, state.ReceiptError }.Where(value =>
                        value.Length > 0
                    )
                );
        return text.Length == 0
            ? new VStack()
            : new Text(text).TextStyle(TextStyleFor()).FontSize(14 * r).ForegroundColor(Ink);
    }

    private Node MessageBubble(ChatMessage message)
    {
        _ = ui.Themes.Current.Value;
        var r = Scale().R;
        var mine =
            session.State is SessionState.SignedIn signed && signed.Profile.Id == message.SenderId;
        var sender = chat.State.Room?.Users.FirstOrDefault(user => user.Id == message.SenderId);
        return new HStack([
            .. mine
                ? new Element[] { new VStack().Grow() }
                : new Element[]
                {
                    Avatar(sender?.ProfileImageUrl, sender?.Name ?? Loc.ChatParticipant, 32 * r),
                },
            new Text(message.Content)
                .TextStyle(TextStyleFor())
                .FontSize(16 * Scale().W)
                .ForegroundColor(mine ? Color.White : Ink)
                .TextOverflow(TextOverflow.Wrap)
                .MaxWidth(264 * Scale().W)
                .Padding(
                    (mine ? 24 : 18) * Scale().W,
                    12 * Scale().H,
                    (mine ? 18 : 24) * Scale().W,
                    12 * Scale().H
                )
                .CornerRadius(24 * r)
                .BackgroundColor(mine ? new Color(60, 94, 245) : theme.Surface),
            .. !mine ? new Element[] { new VStack().Grow() } : [],
        ])
            .Spacing(8 * Scale().W)
            .CrossAlignment(CrossAxisAlignment.Center)
            .Padding(0, 4 * Scale().H);
    }

    private void StopChatRefresh()
    {
        chat.Deactivate();
        _ = chat.FlushAsync();
        chatRefresh?.Cancel();
        chatRefresh?.Dispose();
        chatRefresh = null;
    }

    private void StartChatRefresh()
    {
        if (lifecycle is not null && lifecycle.Activity != ApplicationActivity.Active)
            return;
        chatRefresh = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        chat.Activate(chatRefresh.Token);
        _ = RefreshChatLoop(chatRefresh.Token);
    }

    private async Task RefreshChatLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                Task refresh = Task.CompletedTask;
                await dispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                        refresh = chat.RefreshAsync(token);
                });
                await refresh;
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
