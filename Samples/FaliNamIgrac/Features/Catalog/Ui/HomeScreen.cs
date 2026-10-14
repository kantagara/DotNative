using DotNative;
using DotNative.Composition;
using DotNative.Svg;

namespace FaliNamIgrac;

public sealed partial class HomeScreen : ScreenComponent, IDisposable
{
    private readonly List<IDisposable> subscriptions = [];
    private readonly IApplicationLifecycle? lifecycle;

    private void FollowActivity(ApplicationActivity activity)
    {
        StopChatRefresh();
        if (activity == ApplicationActivity.Active && tab.Value == 2)
            StartChatRefresh();
    }

    public void Dispose()
    {
        chatSearchDelay?.Cancel();
        chatSearchDelay?.Dispose();
        StopChatRefresh();
        if (lifecycle is not null)
            lifecycle.Changed -= FollowActivity;
        foreach (var subscription in subscriptions)
            subscription.Dispose();
        subscriptions.Clear();
    }

    private enum Sheet
    {
        None,
        Sports,
        Sort,
        Filter,
    }

    private readonly CatalogNotifier catalog;
    private readonly SessionNotifier session;
    private readonly Selected<string?> profileName;
    private readonly ListView<Game> games;
    private readonly ListView<Game> myGames;
    private readonly State<int> tab = new(0);
    private readonly State<Sheet> sheet = new(Sheet.None);

    public HomeScreen(
        UiContext ui,
        CatalogNotifier catalog,
        SessionNotifier session,
        IAccountService accountApi,
        IChatService chatApi,
        IApplicationLifecycle? lifecycle = null,
        ICatalogSearchApi? searchApi = null
    )
        : base(ui)
    {
        this.catalog = catalog;
        this.session = session;
        this.lifecycle = lifecycle;
        if (lifecycle is not null)
            lifecycle.Changed += FollowActivity;
        account = new(ui.Dispatcher, session, accountApi, ui.Presentation);
        settings = new(
            ui.Dispatcher,
            ui.Themes,
            ui.Presentation,
            (locale, token) => ui.Preferences.SetAsync("selected_local", locale, token)
        );
        chat = new(ui.Dispatcher, session, chatApi, ui.Presentation, ui.ChatCache);
        chatMessages = new ListView<ChatMessage>([], MessageBubble, message => message.Id)
        {
            FillAvailableHeight = true,
            KeepEndVisible = true,
        };
        chatRooms = new ListView<ChatRoom>([], ChatRoomRow, room => room.Id)
        {
            FillAvailableHeight = true,
        };
        string? displayedRoom = null;
        string? lastMessage = null;
        subscriptions.Add(
            chat.Listen(() =>
            {
                var view = chat.State;
                var messages = view.Messages ?? [];
                var roomChanged = displayedRoom != view.Room?.Id;
                if (roomChanged)
                {
                    showChatInfo.Value = false;
                    confirmLeave.Value = false;
                }
                chatMessages.SetItems(messages);
                var latest = messages.LastOrDefault();
                if (
                    messages.Count > 0
                    && (
                        roomChanged
                        || lastMessage is null
                        || latest?.Id != lastMessage
                            && latest?.SenderId
                                == (session.State as SessionState.SignedIn)?.Profile.Id
                    )
                )
                    chatMessages.ScrollToEnd();
                displayedRoom = view.Room?.Id;
                lastMessage = latest?.Id;
                var userId = (session.State as SessionState.SignedIn)?.Profile.Id ?? "";
                chatRooms.SetItems(
                    (view.Rooms ?? [])
                        .Where(room =>
                            RoomName(room, userId)
                                .Contains(view.Search, StringComparison.CurrentCultureIgnoreCase)
                        )
                        .ToArray()
                );
            })
        );
        var userId = (session.State as SessionState.SignedIn)?.Profile.Id;
        subscriptions.Add(
            session.Listen(() =>
            {
                var next = (session.State as SessionState.SignedIn)?.Profile.Id;
                if (next == userId)
                    return;
                userId = next;
                StopChatRefresh();
                chat.Clear();
                account.Clear();
                profilePage.Value = ProfilePage.Overview;
                tab.Value = 0;
                if (next is not null)
                    _ = PrepareHomeAsync(next);
                else
                    PreparingHome.Value = false;
            })
        );
        profileName = session.Select(state =>
            state is SessionState.SignedIn signed ? signed.Profile.Name : null
        );
        games = new ListView<Game>([], GameCard, game => game.Id) { FillAvailableHeight = true };
        myGames = new ListView<Game>([], MyGameCard, game => game.Id)
        {
            FillAvailableHeight = true,
        };
        myGames.SetItems(catalog.State.MyGames ?? []);
        subscriptions.Add(catalog.Listen(() => myGames.SetItems(catalog.State.MyGames ?? [])));
        if (searchApi is not null)
        {
            var searches = new CatalogSearchNotifier(
                ui.Dispatcher,
                searchApi,
                () => (session.State as SessionState.SignedIn)?.Profile
            );
            ConnectFilters(searches);
            subscriptions.Add(searches);
            PlayerProfile? previous = null;
            void FollowSearchSession()
            {
                var next = (session.State as SessionState.SignedIn)?.Profile;
                if (next == previous)
                    return;
                previous = next;
                var query = searches.State.Query;
                searches.Clear();
                sheet.Value = Sheet.None;
                if (next is not null)
                    _ = searches.ChangeAsync(query, lifetime.Token);
            }
            subscriptions.Add(session.Listen(FollowSearchSession));
            FollowSearchSession();
        }
        settings.Texts = () => new SettingsTexts(ui.Text.SettingsSaveFailed);
        if (userId is not null)
            _ = PrepareHomeAsync(userId);
    }

    public bool CanShowAppOpen => tab.Value < 2 && sheet.Value == Sheet.None;
    public State<bool> PreparingHome { get; } = new(false);
    private int homePreparation;

    private async Task PrepareHomeAsync(string userId)
    {
        var attempt = ++homePreparation;
        // Home is usable while profile extras and chat warm in the background.
        PreparingHome.Value = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await Task.WhenAll(chat.RefreshAsync(timeout.Token), account.LoadAsync(timeout.Token));
        }
        catch (OperationCanceledException) { }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (attempt == homePreparation && !lifetime.IsCancellationRequested)
                    PreparingHome.Value = false;
            });
        }
    }

    public override Element Build()
    {
        FilterLabels = new(
            Loc.FilterMinPrice,
            Loc.FilterMaxPrice,
            Loc.FilterDistanceKm,
            Loc.FilterApply,
            Loc.FilterReset,
            Loc.FilterCancel,
            Loc.FilterInvalidRange,
            Loc.FilterUnavailable,
            Loc.FilterDistanceHint,
            Loc.FilterMyGamesLimit,
            Loc.FilterRetry,
            Loc.FilterNext,
            Loc.FilterPrevious
        );
        chat.Text = new(
            Loc.ChatGroupPhoto,
            Loc.ChatPhotoSelected,
            Loc.ChatDeleteEveryone,
            Loc.ChatConfirmDeleteEveryone,
            Loc.ChatCachedHistory,
            Loc.ChatRead,
            Loc.ChatSent
        );
        var scale = Scale();
        var index = tab.Value;
        var open = sheet.Value;
        var insets = Insets;
        var dark = theme.Mode == ThemeMode.Dark;
        var barHeight = 98 * scale.R + insets.Bottom;
        return new VStack(
            Page(index, open, scale, insets.Top),
            index == 3 && profilePage.Value != ProfilePage.Overview
            || index == 2
                && (chat.State.Room is not null || chat.State.Searching && chat.State.CreatingGroup)
                ? new VStack().Height(insets.Bottom)
                : Bar(index, open, scale, barHeight, dark),
            index == 2 ? ChatOverlay(scale) : new VStack()
        )
            .HeightPercent(100)
            .BackgroundColor(index == 2 ? theme.Background : Background)
            .ClipToBounds(false);
    }

    private Element Page(int index, Sheet open, (float W, float H, float R) scale, float top)
    {
        var page = index switch
        {
            1 => Titled(Loc.MyDates, scale, top, MyDates(scale)),
            2 => ChatPage(scale, top),
            3 => Profile(scale, top),
            _ => Available(open, scale, top),
        };
        return page;
    }

    private Element Available(Sheet open, (float W, float H, float R) scale, float top)
    {
        var view = catalog.State;
        var (w, h, r) = scale;
        Element list = FilteredResults(scale);
        return new VStack([
            Headline(Loc.AvailableDates, r),
            Gap(32 * h),
            Toolbar(view.Sport, scale),
            .. open switch
            {
                Sheet.Sports => new Element[] { SportMenu(scale) },
                Sheet.Sort => new Element[] { SortMenu(scale) },
                Sheet.Filter => new Element[] { FilterMenu(scale) },
                _ => [],
            },
            .. view.Message.Length > 0
                ? new Element[]
                {
                    new Text(view.Message)
                        .TextStyle(TextStyleFor())
                        .FontSize(14 * r)
                        .ForegroundColor(theme.Error),
                }
                : [],
            Gap(24 * h),
            list,
        ]).Padding(16 * w, top, 16 * w, 0).Grow();
    }

    private Element Titled(string title, (float W, float H, float R) scale, float top, Element body)
    {
        var (w, h, r) = scale;
        return new VStack(Headline(title, r), Gap(32 * h), body)
            .Padding(16 * w, top + 8 * h, 16 * w, 0)
            .Grow();
    }

    private Image Icon(string asset, float size) =>
        asset.EndsWith(".svg", StringComparison.Ordinal)
            ? Icon(asset, size, size, null)
            : new Image(FniAssets.Image(asset))
                .Width(size)
                .Height(size)
                .Fit(ImageFit.Cover)
                .CornerRadius(size / 2);

    private Image Icon(string asset, float height, float width, Color? color) =>
        new Image(Svg.Picture(FniAssets.Read(asset), width, height, color))
            .Width(width)
            .Height(height)
            .Fit(ImageFit.Contain);

    private Image ImageIcon(string asset, float size) =>
        new Image(FniAssets.Image(asset))
            .Width(size)
            .Height(size)
            .Fit(ImageFit.Contain)
            .TintColor(Ink);

    private Element Avatar(string? url, string name, float size) =>
        ui.Images?.Get(MediaUrl.Profile(url)) is { } image
            ? new Image(image).Width(size).Height(size).Fit(ImageFit.Cover).CornerRadius(size / 2)
            : Initials(name, size);

    private static string? RoomImage(ChatRoom room, string userId) =>
        !string.IsNullOrWhiteSpace(room.ImageUrl) ? MediaUrl.Group(room.ImageUrl)
        : string.IsNullOrWhiteSpace(room.Title) && room.Participants.Length == 2
            ? room.Users.FirstOrDefault(user => user.Id != userId)?.ProfileImageUrl
        : null;

    private VStack Initials(string name, float size)
    {
        var letters = string.Concat(
            name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])
        );
        return new VStack(
            new Text(letters.ToUpperInvariant())
                .TextStyle(TextStyleFor("Bold"))
                .FontSize(size * 0.36f)
                .ForegroundColor(Color.White)
        )
            .Width(size)
            .Height(size)
            .CornerRadius(size / 2)
            .BackgroundColor(Ink)
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center);
    }

    private VStack IconButton(string asset, float size, Action tap) =>
        new VStack(
            new Image(FniAssets.Image(asset))
                .Width(size * 0.42f)
                .Height(size * 0.42f)
                .TintColor(Ink),
            new Button("", tap)
                .Width(size)
                .Height(size)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
        )
            .Width(size)
            .Height(size)
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center);

    private Element Headline(string text, float r) =>
        new Text(text)
            .TextStyle(TextStyleFor("Bold"))
            .FontSize(32 * r)
            .ForegroundColor(Ink)
            .TextAlignment(TextAlignment.Center)
            .WidthPercent(100);

    private Element Section(string text, float r) =>
        new Text(text).TextStyle(TextStyleFor("Bold")).FontSize(20 * r).ForegroundColor(Ink);

    private Element Centered(string text, float r) =>
        new Text(text)
            .TextStyle(TextStyleFor())
            .FontSize(16 * r)
            .ForegroundColor(Ink)
            .TextAlignment(TextAlignment.Center)
            .WidthPercent(100)
            .Grow();

    private static Element Gap(float size) => new VStack().Width(size).Height(size);

    private void SelectTab(int index)
    {
        filterSearch?.CancelEdit();
        sheet.Value = Sheet.None;
        StopChatRefresh();
        tab.Value = index;
        if (index == 2)
            StartChatRefresh();
        if (index == 3 && account.State.Saved is null)
            _ = account.LoadAsync(lifetime.Token);
        if (
            index == 1
            && !catalog.Busy
            && (catalog.State.MyGames is null || DateTime.UtcNow >= nextMyRefresh)
        )
            LoadMyGames(catalog.State.MySport, 1, catalog.State.MyPassed);
    }

    private void Toggle(Sheet next) => sheet.Value = sheet.Value == next ? Sheet.None : next;

    private void ChooseSport(int id)
    {
        sheet.Value = Sheet.None;
        if (tab.Value == 1)
            LoadMyGames(id, 1, catalog.State.MyPassed);
        else
            Load(id, 1);
    }

    public Action<int, string>? ReportPlayer { get; set; }
    public Action<string?>? OpenEditor { get; set; }

    public IReadOnlyList<Sport> EditorSports =>
        FniSports
            .All.Where(s => account.State.Saved?.Sports.Any(p => p.SportName == s.ApiName) == true)
            .OrderBy(s => s.Id)
            .ToArray();

    public void OpenPlayerChat(AccountData player)
    {
        catalog.BackToList();
        SelectTab(2);
        _ = chat.StartAsync(
            new(player.Id, player.Firstname, player.Lastname, player.Nickname, player.Photo),
            lifetime.Token
        );
    }

    public void OpenGameChat(string id)
    {
        catalog.BackToList();
        SelectTab(2);
        _ = chat.OpenGameAsync(id, lifetime.Token);
    }

    public void OpenNotificationChat(ChatRoom room)
    {
        catalog.BackToList();
        SelectTab(2);
        _ = chat.OpenAsync(room, lifetime.Token);
    }

    public Task RefreshSearchAsync() =>
        filterSearch is { } search
            ? search.LoadAsync(search.State.Query, lifetime.Token)
            : Task.CompletedTask;

    private void Load(int sport, int page)
    {
        if (filterSearch is { } search)
            _ = search.LoadAsync(
                search.State.Query with
                {
                    Sport = sport,
                    Page = page,
                },
                lifetime.Token
            );
    }

    private (float W, float H, float R) Scale()
    {
        if (presentation.Dimensions.Value is not { } size || size.Width <= 0)
            return (1, 1, 1);
        var w = size.Width / 375f;
        var h = Math.Max(size.Height, 700) / 812f;
        return (w, h, Math.Min(w, h));
    }

    private Color Card(bool outer) =>
        theme.Mode == ThemeMode.Dark
            ? outer
                ? theme.Background
                : theme.SecondaryBackground
            : outer
                ? Color.White
                : Color.White;

    private static string? SportIcon(int id)
    {
        foreach (var sport in FniSports.All)
        {
            if (sport.Id != id)
                continue;
            var file = sport.ApiName == "e_games" ? "egames" : sport.ApiName;
            return "sports/" + file + ".png";
        }
        return null;
    }

    private async Task SignOutAsync()
    {
        try
        {
            await session.SignOutAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            catalog.ReportError(FniErrors.UserMessage(presentation.Text, error));
        }
    }
}
