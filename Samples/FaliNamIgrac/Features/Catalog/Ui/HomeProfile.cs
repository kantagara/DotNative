using DotNative;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed partial class HomeScreen
{
    private enum ProfilePage
    {
        Overview,
        Edit,
        Sports,
    }

    private readonly State<ProfilePage> profilePage = new(ProfilePage.Overview);
    private readonly AccountNotifier account;
    private readonly SettingsNotifier settings;
    private IPlacesService? profilePlaces;
    private Func<ProfileTexts>? profileTexts;
    private AccountDeletionNotifier? profileDeletion;
    private Func<string, byte[], CancellationToken, Task>? profilePhotoUpload;

    public void ConfigureProfileFeatures(
        IPlacesService places,
        Func<ProfileTexts> texts,
        AccountDeletionNotifier? deletion,
        Func<string, byte[], CancellationToken, Task>? uploadPhoto = null
    )
    {
        profilePlaces = places;
        profileTexts = texts;
        profileDeletion = deletion;
        profilePhotoUpload = uploadPhoto;
    }

    private Element Profile((float W, float H, float R) scale, float top)
    {
        var r = scale.R;
        var view = account.State;
        var data = view.Saved;
        var name = data is null
            ? profileName.Value ?? ""
            : (data.Firstname + " " + data.Lastname).Trim();
        var page = profilePage.Value;
        Element body = page switch
        {
            ProfilePage.Edit => EditAccount(scale),
            ProfilePage.Sports => EditSports(scale),
            _ => ProfileOverview(scale, name),
        };
        if (profileDeletion?.State.Confirming == true)
            body = DeleteAccountSection(r);
        return new ScrollView(
            new VStack([
                .. page != ProfilePage.Overview
                    ? new Element[]
                    {
                        new HStack(
                            new Button("‹", BackToProfile)
                                .FontSize(32 * r)
                                .ForegroundColor(Ink)
                                .Width(48 * r)
                                .Height(48 * r),
                            new Text(page == ProfilePage.Edit ? Loc.EditProfile : "")
                                .TextStyle(TextStyleFor("Medium"))
                                .FontSize(20 * r)
                                .ForegroundColor(Ink)
                                .Grow()
                        ).CrossAlignment(CrossAxisAlignment.Center),
                    }
                    : [],
                body,
                .. account.Busy ? new Element[] { new Text(Loc.Loading).ForegroundColor(Ink) } : [],
                .. view.Message.Length > 0
                    ? new Element[] { new Text(view.Message).ForegroundColor(Ink) }
                    : [],
                .. data is null && !account.Busy
                    ? new Element[]
                    {
                        ProfileAction(Loc.Retry, () => _ = account.LoadAsync(lifetime.Token), r),
                    }
                    : [],
            ])
                .Spacing(16 * r)
                .Padding(16 * scale.W, top + 24 * scale.H, 16 * scale.W, 24 * r)
        )
            .Grow()
            .Shrink()
            .FlexBasis(0)
            .MinHeight(0);
    }

    private readonly State<bool> profileLanguages = new(false);
    public Action? OpenAdPrivacy { get; set; }
    public Action<bool>? OpenProfileDocument { get; set; }

    private Element ProfileOverview((float W, float H, float R) scale, string name)
    {
        var r = scale.R;
        var data = account.State.Saved;
        var dark = theme.Mode == ThemeMode.Dark;
        var photo = ui.Images?.Get(MediaUrl.Profile(data?.Photo));
        var avatar = new VStack([
            photo is not null
                ? new Image(photo)
                    .Width(100 * r)
                    .Height(100 * r)
                    .Fit(ImageFit.Cover)
                    .CornerRadius(14 * r)
                : new VStack(
                    new Text(
                        string.Concat(
                            name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Take(2)
                                .Select(part => part[0])
                        )
                    )
                        .TextStyle(TextStyleFor("Bold"))
                        .FontSize(24 * r)
                        .ForegroundColor(Color.White)
                )
                    .Width(100 * r)
                    .Height(100 * r)
                    .CornerRadius(14 * r)
                    .BackgroundColor(Accent)
                    .MainAlignment(MainAxisAlignment.Center)
                    .CrossAlignment(CrossAxisAlignment.Center),
            .. ui.PickPhoto is not null && profilePhotoUpload is not null
                ? new Element[]
                {
                    new VStack(
                        new Image(FniAssets.Image("icons/edit.png"))
                            .Width(14 * r)
                            .Height(14 * r)
                            .TintColor(dark ? new Color(38, 38, 38) : new Color(217, 217, 217)),
                        new Button(
                            "",
                            () =>
                                _ = account.UploadPhotoAsync(
                                    ui.PickPhoto,
                                    profilePhotoUpload,
                                    (url, bytes) =>
                                        ui.Images?.RememberAsync(MediaUrl.Profile(url), bytes)
                                        ?? Task.CompletedTask,
                                    lifetime.Token
                                )
                        )
                            .Position(PositionMode.Absolute)
                            .Insets(new(0, 0, 0, 0))
                    )
                        .Width(40 * r)
                        .Height(40 * r)
                        .CornerRadius(7 * r)
                        .BackgroundColor(dark ? new Color(217, 217, 217) : new Color(38, 38, 38))
                        .MainAlignment(MainAxisAlignment.Center)
                        .CrossAlignment(CrossAxisAlignment.Center)
                        .Position(PositionMode.Absolute)
                        .Right(0)
                        .Bottom(0),
                }
                : [],
        ])
            .Width(100 * r)
            .Height(100 * r);
        return new VStack([
            new HStack(
                avatar,
                new VStack(
                    new Text($"{Loc.Hello}, {data?.Firstname ?? name.Split(' ')[0]}")
                        .TextStyle(TextStyleFor("Bold"))
                        .FontSize(24 * r)
                        .ForegroundColor(Ink),
                    new Text(data?.Nickname ?? "")
                        .TextStyle(TextStyleFor("Medium"))
                        .FontSize(12 * r)
                        .ForegroundColor(Ink),
                    new Text(data?.Phone ?? "")
                        .TextStyle(TextStyleFor("Medium"))
                        .FontSize(12 * r)
                        .ForegroundColor(Ink)
                )
                    .Grow()
                    .Shrink()
                    .FlexBasis(0)
                    .MinWidth(0)
            )
                .Spacing(21 * scale.W)
                .CrossAlignment(CrossAxisAlignment.Center),
            Gap(24 * r),
            Section(Loc.Choose, r),
            Gap(16 * scale.H),
            ProfileMenuRow(Loc.AddOrChangeSport, () => profilePage.Value = ProfilePage.Sports, r),
            .. ProfileAdminAccess?.Invoke() == true
                ? new Element[]
                {
                    ProfileMenuRow(Loc.Reporting08ReportsTitle, () => OpenModeration?.Invoke(), r),
                }
                : [],
            ProfileMenuRow(Loc.EditProfile, () => profilePage.Value = ProfilePage.Edit, r),
            new HStack(
                new Text(Loc.DarkMode)
                    .TextStyle(TextStyleFor())
                    .FontSize(16 * r)
                    .ForegroundColor(Ink)
                    .Grow(),
                new VStack(
                    new VStack()
                        .Width(28 * r)
                        .Height(28 * r)
                        .CornerRadius(14 * r)
                        .BackgroundColor(Color.White)
                        .Position(PositionMode.Absolute)
                        .Top(2 * r)
                        .Left((dark ? 24 : 2) * r),
                    new Button(
                        "",
                        () =>
                            _ = settings.SetThemeAsync(
                                dark ? ThemeMode.Light : ThemeMode.Dark,
                                lifetime.Token
                            )
                    )
                        .Position(PositionMode.Absolute)
                        .Insets(new(0, 0, 0, 0))
                )
                    .Width(54 * r)
                    .Height(32 * r)
                    .CornerRadius(16 * r)
                    .BackgroundColor(dark ? Accent : new Color(120, 120, 128))
            )
                .MinHeight(56 * r)
                .CrossAlignment(CrossAxisAlignment.Center)
                .BorderBottom(new BorderSide(0.5f, new Color(137, 137, 137))),
            ProfileMenuRow(Loc.Language, () => profileLanguages.Value = !profileLanguages.Value, r),
            .. profileLanguages.Value
                ? new Element[]
                {
                    new HStack(
                        ProfileLanguage("Srpski", "sr", "serbia", r),
                        ProfileLanguage("English", "en", "uk", r),
                        ProfileLanguage("Русский", "ru", "ru", r)
                    )
                        .MainAlignment(MainAxisAlignment.SpaceEvenly)
                        .Padding(0, 16 * r),
                }
                : [],
            ProfileMenuRow(Loc.TermsOfService, () => OpenProfileDocument?.Invoke(false), r),
            ProfileMenuRow(Loc.PrivacyPolicyLabel, () => OpenProfileDocument?.Invoke(true), r),
            .. OpenAdPrivacy is not null
                ? new Element[] { ProfileMenuRow(Loc.AdPlacementPrivacyOptions, OpenAdPrivacy, r) }
                : [],
            ProfileMenuRow(Loc.SignOut, () => _ = SignOutAsync(), r),
            new VStack(DeleteAccountSection(r)).Padding(0, 16 * r),
            ProfileStatus?.Invoke() ?? new VStack(),
            .. settings.State.Length > 0
                ? new Element[]
                {
                    new Text(settings.State).ForegroundColor(theme.Error),
                    ProfileAction(Loc.Retry, () => _ = settings.RetryAsync(lifetime.Token), r),
                }
                : [],
        ]);
    }

    private Element ProfileMenuRow(string label, Action tap, float r) =>
        new HStack(
            new Text(label)
                .TextStyle(TextStyleFor("Medium"))
                .FontWeight(FontWeight.SemiBold)
                .FontSize(16 * r)
                .ForegroundColor(Ink)
                .Grow(),
            new Image(FniAssets.Image("icons/arrow_right.png"))
                .Width(20 * r)
                .Height(20 * r)
                .TintColor(Ink),
            new Button("", tap).Position(PositionMode.Absolute).Insets(new(0, 0, 0, 0))
        )
            .MinHeight(56 * r)
            .CrossAlignment(CrossAxisAlignment.Center)
            .BorderBottom(new BorderSide(0.5f, new Color(137, 137, 137)));

    private Element ProfileLanguage(string label, string locale, string asset, float r) =>
        new VStack(
            new Image(FniAssets.Image($"flags/{asset}.png"))
                .Width(40 * r)
                .Height(40 * r)
                .Fit(ImageFit.Cover)
                .CornerRadius(20 * r),
            new Text(label)
                .TextStyle(TextStyleFor())
                .FontSize(12 * r)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Center),
            new Button("", () => _ = settings.SetLanguageAsync(locale, lifetime.Token))
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0))
        )
            .CrossAlignment(CrossAxisAlignment.Center)
            .Spacing(8 * r)
            .Padding(8 * r)
            .BackgroundColor(
                presentation.Locale.Value == locale
                    ? theme.Primary with
                    {
                        A = 24,
                    }
                    : Color.Transparent
            )
            .CornerRadius(12 * r);

    public Func<bool>? ProfileAdminAccess { get; set; }
    public Action? OpenModeration { get; set; }
    public Func<Element>? ProfileStatus { get; set; }

    private void BackToProfile()
    {
        account.CancelEdit();
        profilePage.Value = ProfilePage.Overview;
    }

    private async Task SaveProfileAsync()
    {
        await account.SaveAsync(lifetime.Token);
        await ui.Dispatcher.InvokeAsync(() =>
        {
            if (account.State.Message == Loc.EditProfileSuccess && !account.Busy)
                profilePage.Value = ProfilePage.Overview;
        });
    }

    private Element ProfileSave(float r, bool enabled = true) => new Button(
            Loc.Save,
            () =>
            {
                if (enabled && !account.Busy)
                    _ = SaveProfileAsync();
            }
        ).TextStyle(TextStyleFor("Medium")).FontSize(16 * r).TextAlignment(TextAlignment.Center).ForegroundColor(Color.White).BackgroundColor(Accent).CornerRadius(16 * r).Height(56 * r).WidthPercent(100).Opacity(enabled && !account.Busy ? 1 : 0.3f);

    private Element EditAccount((float W, float H, float R) scale)
    {
        if (account.State.Draft is not { } draft)
            return new VStack();
        var r = scale.R;
        Element Gender(string value, string label) =>
            new Button(
                (draft.Gender == value ? "◉  " : "○  ") + label,
                () => account.Edit(data => data with { Gender = value })
            )
                .TextStyle(TextStyleFor())
                .FontSize(16 * r)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Left)
                .Height(48 * r)
                .Grow();
        return new VStack(
            Gap(16 * scale.H),
            ProfileField(
                Loc.FirstName,
                draft.Firstname,
                value => account.Edit(data => data with { Firstname = value }),
                r
            ),
            ProfileField(
                Loc.LastName,
                draft.Lastname,
                value => account.Edit(data => data with { Lastname = value }),
                r
            ),
            new Text(draft.Nickname)
                .TextStyle(TextStyleFor())
                .FontSize(16 * r)
                .ForegroundColor(theme.MutedForeground)
                .Padding(16 * r)
                .Height(56 * r)
                .BackgroundColor(theme.Surface)
                .CornerRadius(16 * r),
            new HStack(
                new Text(draft.BirthDate?.Split('T')[0] ?? Loc.DateOfBirth)
                    .TextStyle(TextStyleFor())
                    .FontSize(16 * r)
                    .ForegroundColor(Ink)
                    .Grow(),
                new Text("\U000f06bb")
                    .TextAlignment(TextAlignment.Center)
                    .TextStyle(TextStyleFor("Icons"))
                    .FontSize(24 * r)
                    .ForegroundColor(Ink),
                new Button("", () => _ = ChooseProfileBirthDateAsync())
                    .Position(PositionMode.Absolute)
                    .Insets(new(0, 0, 0, 0))
            )
                .Padding(16 * r)
                .Height(56 * r)
                .BackgroundColor(theme.Surface)
                .CornerRadius(16 * r)
                .CrossAlignment(CrossAxisAlignment.Center),
            ProfileField(
                Loc.PhoneNumber,
                draft.Phone ?? "",
                value => account.Edit(data => data with { Phone = value }),
                r
            ),
            ProfileLocation(r),
            new HStack(Gender("male", Loc.Man), Gender("female", Loc.Woman)),
            ProfileField(
                Loc.Biography,
                draft.Bio ?? "",
                value => account.Edit(data => data with { Bio = value }),
                r,
                true
            ),
            Gap(16 * scale.H),
            ProfileSave(r),
            Gap(16 * scale.H)
        ).Spacing(16 * scale.H);
    }

    private readonly State<int> profileSportCategory = new(0);
    private readonly State<string> profileSportSearch = new("");
    private readonly State<bool> profileLocationOpen = new(false);

    private Element EditSports((float W, float H, float R) scale)
    {
        if (account.State.Draft is not { } draft)
            return new VStack();
        var r = scale.R;
        var category = FniSports
            .All.Where(sport => (sport.Id <= 8 ? 0 : 1) == profileSportCategory.Value)
            .ToArray();
        var allSelected = category.All(sport =>
            draft.Sports.Any(item => item.SportName == sport.ApiName)
        );
        Element Category(int index, string label, string asset) =>
            new VStack(
                new Image(FniAssets.Image(asset))
                    .Height(88 * scale.H)
                    .WidthPercent(100)
                    .Fit(ImageFit.Cover),
                new Text(label)
                    .TextStyle(TextStyleFor("Medium"))
                    .FontSize(14 * r)
                    .ForegroundColor(Ink)
                    .TextAlignment(TextAlignment.Center)
                    .Padding(4 * r, 8 * r),
                new Button("", () => profileSportCategory.Value = index)
                    .Position(PositionMode.Absolute)
                    .Insets(new(0, 0, 0, 0))
            )
                .Grow()
                .Shrink()
                .FlexBasis(0)
                .MinWidth(0)
                .CornerRadius(16 * r)
                .ClipToBounds(true)
                .BackgroundColor(theme.Surface)
                .Opacity(profileSportCategory.Value == index ? 1 : 0.4f);
        return new VStack([
            Headline(Loc.ChooseSport, r),
            Gap(8 * scale.H),
            new Text(Loc.ChooseSportBody)
                .TextStyle(TextStyleFor())
                .FontSize(14 * r)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Center)
                .TextOverflow(TextOverflow.Wrap),
            Gap(24 * scale.H),
            new HStack(
                Category(0, Loc.SportWithBall, "sports/ball_sports.png"),
                Category(1, Loc.OutdoorSports, "sports/outdoor_sports.jpeg")
            ).Spacing(16 * scale.W),
            Gap(24 * scale.H),
            ProfileField(
                Loc.Search,
                profileSportSearch.Value,
                value => profileSportSearch.Value = value,
                r
            ),
            Gap(32 * scale.H),
            new HStack(
                new Text(Loc.SelectAllSportsInGroup)
                    .TextStyle(TextStyleFor())
                    .FontSize(16 * r)
                    .ForegroundColor(Ink)
                    .Grow()
                    .Shrink()
                    .FlexBasis(0)
                    .MinWidth(0)
                    .TextOverflow(TextOverflow.Wrap),
                ProfileToggle(
                    allSelected,
                    () =>
                    {
                        foreach (var sport in category)
                            if (
                                draft.Sports.Any(item => item.SportName == sport.ApiName)
                                != !allSelected
                            )
                                account.ToggleSport(sport.ApiName);
                    },
                    r
                )
            ).Spacing(12 * r).CrossAlignment(CrossAxisAlignment.Center),
            Gap(16 * scale.H),
            .. category
                .Where(sport =>
                    FniSports
                        .Name(sport.Id, Loc)
                        .Contains(
                            profileSportSearch.Value,
                            StringComparison.CurrentCultureIgnoreCase
                        )
                )
                .Select(sport => ProfileSportRow(sport, r)),
            Gap(32 * scale.H),
            ProfileSave(r, draft.Sports.Length > 0),
        ]);
    }

    private Element ProfileToggle(bool selected, Action tap, float r) =>
        new VStack(
            new VStack()
                .Width(28 * r)
                .Height(28 * r)
                .CornerRadius(14 * r)
                .BackgroundColor(Color.White)
                .Position(PositionMode.Absolute)
                .Top(2 * r)
                .Left((selected ? 24 : 2) * r),
            new Button("", tap).Position(PositionMode.Absolute).Insets(new(0, 0, 0, 0))
        )
            .Width(54 * r)
            .Height(32 * r)
            .CornerRadius(16 * r)
            .BackgroundColor(selected ? Accent : theme.MutedForeground);

    private Element ProfileSportRow(Sport sport, float r)
    {
        var selected = account.State.Draft!.Sports.FirstOrDefault(item =>
            item.SportName == sport.ApiName
        );
        return new HStack(
            new VStack(
                new Image(FniAssets.Image(SportIcon(sport.Id)!))
                    .Width(44 * r)
                    .Height(44 * r)
                    .Fit(ImageFit.Contain)
            )
                .Width(54 * r)
                .Height(54 * r)
                .CornerRadius(27 * r)
                .BackgroundColor(Color.Black)
                .Border(2 * r, Color.White)
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center),
            new Text(FniSports.Name(sport.Id, Loc))
                .TextStyle(TextStyleFor())
                .FontSize(14 * r)
                .ForegroundColor(Ink)
                .Grow()
                .Shrink()
                .FlexBasis(0)
                .MinWidth(0),
            new Button("", () => account.ToggleSport(sport.ApiName))
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0)),
            new Button(
                selected?.NotificationType == "ON" ? "\ue450" : "\ue452",
                () =>
                {
                    if (selected is not null)
                        account.ToggleNotifications(sport.ApiName);
                }
            ).TextAlignment(TextAlignment.Center).TextStyle(TextStyleFor("Icons")).FontSize(24 * r).ForegroundColor(selected is null ? theme.MutedForeground : Ink).Width(40 * r).Height(48 * r),
            new Button(
                selected is null ? "\ue158" : "\ue157",
                () => account.ToggleSport(sport.ApiName)
            )
                .TextAlignment(TextAlignment.Center)
                .TextStyle(TextStyleFor("Icons"))
                .FontSize(24 * r)
                .ForegroundColor(selected is null ? Ink : Accent)
                .Width(40 * r)
                .Height(48 * r)
        )
            .CrossAlignment(CrossAxisAlignment.Center)
            .Spacing(8 * r)
            .Padding(4 * r, 4 * r, 12 * r, 4 * r)
            .Height(66 * r)
            .BackgroundColor(theme.Surface)
            .CornerRadius(33 * r)
            .Margin(0, 0, 0, 8 * r);
    }

    private Element ProfileLocation(float r)
    {
        if (profilePlaces is null || profileTexts is null)
            return new Text(account.State.Draft?.Address ?? "").ForegroundColor(Ink);
        var state = account.State;
        if (!profileLocationOpen.Value)
            return new Button(
                state.Draft?.Address ?? Loc.EnterLocation,
                () => profileLocationOpen.Value = true
            )
                .TextStyle(TextStyleFor())
                .FontSize(16 * r)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Left)
                .Padding(16 * r)
                .Height(56 * r)
                .BackgroundColor(theme.Surface)
                .CornerRadius(16 * r);
        return new VStack([
            new Text(state.Draft?.Address ?? "").ForegroundColor(Ink),
            ProfileField(Loc.EnterLocation, state.LocationQuery, account.SetLocationQuery, r),
            ProfileAction(
                profileTexts().SearchLocation,
                () => _ = account.SearchPlacesAsync(profilePlaces, lifetime.Token),
                r
            ),
            .. state.Suggestions?.Select(place =>
                (Element)ProfileAction(
                    place.Description,
                    () => _ = account.SelectPlaceAsync(profilePlaces, place, lifetime.Token),
                    r
                )
            )
                ?? [],
            .. state.Suggestions is { Count: 0 }
                ? new Element[] { new Text(profileTexts().NoLocations).ForegroundColor(Ink) }
                : [],
        ]).Spacing(10 * r);
    }

    private async Task ChooseProfileBirthDateAsync()
    {
        if (account.Busy || account.State.Draft is not { } draft)
            return;
        var signed = session.State;
        try
        {
            var selected = await ui.ShowDatePickerAsync(
                new DatePickerOptions
                {
                    Locale = presentation.Locale.Value switch
                    {
                        "sr" => "sr-Latn-RS",
                        "ru" => "ru-RU",
                        _ => "en-US",
                    },
                    InitialDate = AccountValidation.TryBirthDate(draft.BirthDate, out var birth)
                        ? birth
                        : null,
                    MinimumDate = new(1930, 1, 1),
                    MaximumDate = DateOnly.FromDateTime(DateTime.Today),
                    Title = Loc.ChooseDate,
                    ConfirmText = Loc.Save,
                    CancelText = Loc.Cancel,
                },
                lifetime.Token
            );
            await ui.Dispatcher.InvokeAsync(() =>
            {
                if (
                    selected is { } date
                    && ReferenceEquals(signed, session.State)
                    && ReferenceEquals(draft, account.State.Draft)
                    && !lifetime.IsCancellationRequested
                )
                    account.SetBirthDate(date);
            });
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            await ui.Toast.ShowAsync(FniErrors.UserMessage(Loc, error), lifetime.Token);
        }
    }

    private Element DeleteAccountSection(float r)
    {
        if (profileTexts is null)
            return new VStack();
        var text = profileTexts();
        if (profileDeletion is null)
            return new Text(text.DeleteUnavailable).ForegroundColor(theme.MutedForeground);
        var state = profileDeletion.State;
        return new VStack([
            .. !state.Confirming
                ? new Element[]
                {
                    ProfileAction(
                        text.DeleteAccount,
                        () =>
                        {
                            if (!account.Busy)
                                profileDeletion.AskConfirmation();
                        },
                        r
                    ),
                }
                :
                [
                    new Text(text.DeleteConfirmation).ForegroundColor(Ink),
                    ProfileAction(
                        profileDeletion.Busy ? Loc.Loading
                            : state.Failed ? Loc.Retry
                            : text.DeleteAccount,
                        () =>
                        {
                            if (!account.Busy)
                                _ = profileDeletion.ConfirmAsync(lifetime.Token);
                        },
                        r
                    ),
                    .. state.Stage < AccountDeletionStage.BackendDeleted
                        ? new Element[] { ProfileAction(Loc.Cancel, profileDeletion.Cancel, r) }
                        : [],
                ],
            .. state.Failed
                ? new Element[]
                {
                    new Text(
                        state.Stage >= AccountDeletionStage.BackendDeleted
                            ? text.DeletePartial
                            : text.DeleteFailed
                    ).ForegroundColor(Ink),
                }
                : [],
        ]).Spacing(10 * r);
    }

    private Element ProfileField(
        string label,
        string value,
        Action<string> change,
        float r,
        bool multiline = false
    ) =>
        new TextField(value, change)
        {
            Placeholder = label,
            Input = new()
            {
                IsMultiline = multiline,
                Keyboard = label == Loc.PhoneNumber ? TextKeyboard.Telephone : TextKeyboard.Text,
            },
        }
            .TextStyle(TextStyleFor())
            .FontSize(16 * r)
            .ForegroundColor(Ink)
            .Padding(16 * r)
            .BackgroundColor(theme.Surface)
            .CornerRadius(16 * r)
            .Height((multiline ? 128 : 56) * r);

    private Button ProfileAction(string label, Action tap, float r) => new Button(
            label,
            () =>
            {
                if (profileDeletion?.Busy != true)
                    tap();
            }
        ).TextStyle(TextStyleFor("Medium")).FontSize(16 * r).ForegroundColor(Ink).BackgroundColor(theme.Surface).CornerRadius(14 * r).Padding(14 * r).MinHeight(Math.Max(48, 54 * r)).WidthPercent(100);

    private Element ProfileChoice(string label, bool selected, Action tap, float r) =>
        ProfileAction((selected ? "✓  " : "") + label, tap, r);
}
