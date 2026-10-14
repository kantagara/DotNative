using DotNative;

namespace FaliNamIgrac;

public sealed partial class HomeScreen
{
    private CancellationTokenSource? chatSearchDelay;
    private readonly State<bool> renameChat = new(false);

    private Button ChatIcon(string glyph, Action click, float r) =>
        new Button(glyph, click)
            .TextStyle(TextStyleFor("Icons"))
            .FontSize(24 * r)
            .TextAlignment(TextAlignment.Center)
            .ForegroundColor(Ink)
            .Width(48 * r)
            .Height(48 * r);

    private void SearchChatUsers(string value)
    {
        chat.SetSearch(value);
        chatSearchDelay?.Cancel();
        chatSearchDelay?.Dispose();
        chatSearchDelay = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = SearchAfterDelay(chatSearchDelay.Token);
    }

    private async Task SearchAfterDelay(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            Task search = Task.CompletedTask;
            await dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested && chat.State.Searching)
                    search = chat.SearchAsync(token);
            });
            await search;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private Element ChatSearchField((float W, float H, float R) s) => new HStack(
            new TextField(chat.State.Search, SearchChatUsers)
            {
                Placeholder = Loc.Search,
                Input = new() { PlaceholderColor = AuthHint },
            }
                .TextStyle(TextStyleFor())
                .FontSize(16 * s.W)
                .ForegroundColor(Ink)
                .Padding(16 * s.W, 0)
                .Height(48 * s.H)
                .Grow()
                .Shrink()
                .FlexBasis(0)
                .MinWidth(0)
                .WithKey("chat-user-search"),
            ChatIcon("\ue567", () => _ = chat.SearchAsync(lifetime.Token), s.R)
                .ForegroundColor(Color.White)
                .BackgroundColor(new Color(38, 38, 38))
                .CornerRadius(14 * s.R)
        ).CrossAlignment(CrossAxisAlignment.Center).Padding(2 * s.R).BackgroundColor(Card(false)).CornerRadius(16 * s.R);

    private Element ChatUserRow(ChatUser user, (float W, float H, float R) s, bool group)
    {
        var selected = chat.State.SelectedUsers?.Any(item => item.Id == user.Id) == true;
        return new VStack(
            new HStack(
                group
                    ? new Text(selected ? "\ue503" : "\ue504")
                        .TextStyle(TextStyleFor("Icons"))
                        .FontSize(26 * s.R)
                        .ForegroundColor(selected ? Accent : AuthHint)
                        .Width(32 * s.W)
                    : new VStack(),
                Avatar(user.ProfileImageUrl, user.Name, 40 * s.R),
                new VStack(
                    new Text(user.Name)
                        .TextStyle(TextStyleFor())
                        .FontSize(16 * s.W)
                        .ForegroundColor(Ink)
                        .MaxLines(1)
                        .TextOverflow(TextOverflow.EllipsisEnd),
                    new Text(user.Nickname ?? "")
                        .TextStyle(TextStyleFor())
                        .FontSize(14 * s.W)
                        .ForegroundColor(AuthHint)
                )
                    .Grow()
                    .Shrink()
                    .FlexBasis(0)
                    .MinWidth(0)
                    .Spacing(3 * s.H)
            )
                .Spacing(16 * s.W)
                .CrossAlignment(CrossAxisAlignment.Center)
                .Padding(16 * s.W, 8 * s.H),
            new Button(
                "",
                () =>
                {
                    if (group)
                        chat.ToggleGroupUser(user);
                    else
                        _ = chat.StartAsync(user, lifetime.Token);
                }
            ).Enabled(!chat.State.Loading).Position(PositionMode.Absolute).Insets(new(0, 0, 0, 0))
        ).MinHeight(64 * s.H);
    }

    private Element ChatSearchResults((float W, float H, float R) s, bool group)
    {
        var view = chat.State;
        if (view.Loading)
            return new VStack(BusyIndicator(Accent))
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .Grow();
        if (view.Users is not { Count: > 0 } users)
            return Centered(view.Users is null ? Loc.SearchUsersLabel : Loc.NoUsersFound, s.R);
        return new ScrollView(
            new VStack(
                users
                    .Where(user =>
                        !group || view.SelectedUsers?.Any(item => item.Id == user.Id) != true
                    )
                    .Select(user => ChatUserRow(user, s, group))
                    .ToArray()
            )
        )
            .Grow()
            .Shrink()
            .FlexBasis(0)
            .MinHeight(0);
    }

    private Element ChatOverlay((float W, float H, float R) s)
    {
        var view = chat.State;
        if (renameChat.Value || confirmDeleteChat.Value || confirmLeave.Value)
        {
            void Close()
            {
                renameChat.Value = false;
                confirmDeleteChat.Value = false;
                confirmLeave.Value = false;
            }
            async Task Apply()
            {
                if (renameChat.Value)
                    await chat.RenameGroupAsync(lifetime.Token);
                else if (confirmDeleteChat.Value)
                    await chat.DeleteForEveryoneAsync(lifetime.Token);
                else
                    await chat.LeaveGroupAsync(lifetime.Token);
                if (!disposed && chat.State.Error.Length == 0)
                    Close();
            }
            return new VStack(
                new Button("", Close)
                    .BackgroundColor(new Color(0, 0, 0, 120))
                    .Position(PositionMode.Absolute)
                    .Insets(new(0, 0, 0, 0)),
                new VStack(new Text(
                        renameChat.Value ? Loc.GroupName
                        : confirmDeleteChat.Value ? Loc.ConfirmDeleteChatTitle
                        : Loc.LeaveGroup
                    ).TextStyle(TextStyleFor("Bold")).FontSize(22 * s.W).ForegroundColor(Ink), renameChat.Value ? new TextField(view.GroupName, chat.SetGroupName).TextStyle(TextStyleFor()).FontSize(16 * s.W).ForegroundColor(Ink).Height(56 * s.H).Padding(12 * s.W).BackgroundColor(theme.Surface).CornerRadius(12 * s.R) : new Text(confirmDeleteChat.Value ? Loc.ConfirmDeleteChatBody : Loc.ChatConfirmLeave).TextStyle(TextStyleFor()).FontSize(16 * s.W).ForegroundColor(Ink), ChatError(s.R), new HStack(new VStack().Grow(), new Button(Loc.Cancel, Close).TextStyle(TextStyleFor("Medium")).ForegroundColor(Accent).Height(48 * s.H), view.Loading ? BusyIndicator(Accent) : new Button(renameChat.Value ? Loc.Save : Loc.ContinueLabel, () => _ = Apply()).TextStyle(TextStyleFor("Medium")).ForegroundColor(Accent).Height(48 * s.H).Enabled(!renameChat.Value || view.GroupName.Trim().Length >= 3)).Spacing(20 * s.W).CrossAlignment(CrossAxisAlignment.Center)).Spacing(16 * s.H).Padding(24 * s.R).Width(320 * s.W).BackgroundColor(Card(false)).CornerRadius(24 * s.R)
            )
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0));
        }
        if (view.Searching && !view.CreatingGroup)
        {
            var height = Math.Max(
                180,
                Math.Min(
                    450 * s.H,
                    (presentation.Dimensions.Value?.Height ?? 812)
                        - Insets.Top
                        - Insets.Keyboard
                        - 48
                )
            );
            return new VStack(
                new Button("", chat.Back)
                    .BackgroundColor(new Color(0, 0, 0, 100))
                    .Position(PositionMode.Absolute)
                    .Insets(new(0, 0, 0, 0)),
                new VStack(
                    ChatSearchField(s),
                    Gap(12 * s.H),
                    ChatSearchResults(s, false),
                    ChatError(s.R)
                )
                    .Padding(16 * s.R)
                    .Height(height)
                    .WidthPercent(100)
                    .BackgroundColor(
                        theme.Mode == ThemeMode.Dark ? new Color(37, 37, 37) : Color.White
                    )
                    .CornerRadii(new(16 * s.R, 16 * s.R, 0, 0))
                    .Position(PositionMode.Absolute)
                    .Left(0)
                    .Bottom(Math.Max(Insets.Bottom, Insets.Keyboard))
            )
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0));
        }
        if (!showChatCreateMenu.Value)
            return new VStack();
        Element Item(string glyph, string label, Action action) =>
            new VStack(
                new HStack(
                    new Text(glyph)
                        .TextStyle(TextStyleFor("Icons"))
                        .FontSize(24 * s.R)
                        .ForegroundColor(Ink),
                    new Text(label)
                        .TextStyle(TextStyleFor())
                        .FontSize(16 * s.W)
                        .ForegroundColor(Ink)
                )
                    .Spacing(8 * s.W)
                    .CrossAlignment(CrossAxisAlignment.Center)
                    .Height(48 * s.H)
                    .Padding(16 * s.W, 0),
                new Button("", action).Position(PositionMode.Absolute).Insets(new(0, 0, 0, 0))
            );
        return new VStack(
            new Button("", () => showChatCreateMenu.Value = false)
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0)),
            new VStack(
                Item(
                    "\ue155",
                    Loc.StartNewChat,
                    () =>
                    {
                        showChatCreateMenu.Value = false;
                        chat.SearchUsers();
                    }
                ),
                Item(
                    "\ue2eb",
                    Loc.CreateGroup,
                    () =>
                    {
                        showChatCreateMenu.Value = false;
                        chat.SearchUsers(true);
                    }
                )
            ).BackgroundColor(theme.Background).CornerRadius(16 * s.R).Shadow(new Color(0, 0, 0, 70), 8).ClipToBounds(false).Width(250 * s.W).Position(PositionMode.Absolute).Right(16 * s.W).Top(Insets.Top + 64 * s.H)
        )
            .Position(PositionMode.Absolute)
            .Insets(new(0, 0, 0, 0));
    }

    private VStack ChatGroupEditor((float W, float H, float R) s)
    {
        var view = chat.State;
        var valid = view.GroupName.Trim().Length >= 3 && view.SelectedUsers is { Count: >= 2 };
        return new VStack(
            new HStack(ChatIcon("\ue092", chat.Back, s.R), new VStack().Grow(), new Button(
                    view.Loading ? ""
                        : view.EditingGroup ? Loc.Save
                        : Loc.Create,
                    () => _ = chat.CreateGroupAsync(lifetime.Token)
                ).TextStyle(TextStyleFor("Medium")).FontSize(16 * s.W).ForegroundColor(valid ? Accent : AuthHint).Enabled(valid && !view.Loading).Height(56 * s.H).MinWidth(72 * s.W), view.Loading ? BusyIndicator(Accent) : new VStack()).CrossAlignment(CrossAxisAlignment.Center),
            new ScrollView(
                new VStack(
                    new Text(view.EditingGroup ? Loc.ChatEditGroup : Loc.CreateGroup)
                        .TextStyle(TextStyleFor("Bold"))
                        .FontSize(32 * s.W)
                        .ForegroundColor(Ink),
                    Gap(24 * s.H),
                    new VStack(
                        view.GroupPhoto is { } photo
                            ? new Image(ImageSource.FromBytes(photo))
                                .Fit(ImageFit.Cover)
                                .Width(68 * s.W)
                                .Height(68 * s.W)
                                .CornerRadius(34 * s.W)
                            : new VStack(
                                new Image(FniAssets.Image("icon/group_chat.png"))
                                    .Width(28 * s.W)
                                    .Height(28 * s.W)
                            )
                                .MainAlignment(MainAxisAlignment.Center)
                                .CrossAlignment(CrossAxisAlignment.Center)
                                .Width(68 * s.W)
                                .Height(68 * s.W)
                                .CornerRadius(34 * s.W)
                                .BackgroundColor(Accent),
                        ChatIcon(
                                "\uf00d",
                                () =>
                                {
                                    if (ui.PickPhoto is not null)
                                        _ = chat.PickGroupPhotoAsync(ui.PickPhoto, lifetime.Token);
                                },
                                s.R
                            )
                            .Enabled(
                                ui.PickPhoto is not null && !view.Loading && !view.EditingGroup
                            )
                            .FontSize(18 * s.R)
                            .Width(32 * s.W)
                            .Height(32 * s.H)
                            .CornerRadius(7 * s.R)
                            .BackgroundColor(Ink)
                            .ForegroundColor(theme.Background)
                            .Position(PositionMode.Absolute)
                            .Right(0)
                            .Bottom(0)
                    ).Width(75 * s.W).Height(75 * s.H),
                    Gap(24 * s.H),
                    new TextField(view.GroupName, chat.SetGroupName)
                    {
                        Placeholder = Loc.GroupName,
                        Input = new() { PlaceholderColor = AuthHint },
                    }
                        .TextStyle(TextStyleFor())
                        .FontSize(16 * s.W)
                        .ForegroundColor(Ink)
                        .Padding(16 * s.W, 0)
                        .Height(56 * s.H)
                        .BackgroundColor(theme.Surface)
                        .Border(1, new Color(246, 246, 246))
                        .CornerRadius(16 * s.R)
                        .OnState(
                            InteractionState.Focused,
                            new(BorderWidth: 1, BorderColor: Accent)
                        ),
                    Gap(24 * s.H),
                    ChatSearchField(s),
                    Gap(24 * s.H),
                    new VStack(
                        (view.SelectedUsers ?? [])
                            .Select(user => ChatUserRow(user, s, true))
                            .ToArray()
                    ),
                    ChatError(s.R),
                    new VStack(ChatSearchResults(s, true)).Height(320 * s.H)
                )
            ).Grow().Shrink().FlexBasis(0).MinHeight(0)
        )
            .Grow()
            .Shrink()
            .FlexBasis(0)
            .MinHeight(0);
    }
}
