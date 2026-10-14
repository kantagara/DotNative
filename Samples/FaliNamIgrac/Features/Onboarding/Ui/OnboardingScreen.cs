using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class OnboardingScreen : ScreenComponent
{
    private readonly IOnboardingController onboarding;
    private readonly RegistrationFormNotifier form;
    private readonly SessionNotifier session;
    private readonly State<bool> pickingPhoto = new(false);
    private int photoRevision;
    private CancellationTokenSource? viewLifetime;
    private CancellationToken ViewToken => viewLifetime?.Token ?? lifetime.Token;
    private readonly State<bool> restoring = new(false);
    private readonly State<string?> restoreError = new(null);
    private RegistrationIdentity? initializedIdentity;

    public OnboardingScreen(
        UiContext ui,
        IOnboardingController onboarding,
        RegistrationFormNotifier form,
        SessionNotifier session
    )
        : base(ui)
    {
        this.onboarding = onboarding;
        this.form = form;
        this.session = session;
    }

    public void Initialize(RegistrationIdentity value)
    {
        photoRevision++;
        viewLifetime?.Cancel();
        viewLifetime?.Dispose();
        viewLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        onboarding.Initialize(value);
        if (initializedIdentity != value)
        {
            initializedIdentity = value;
            _ = RestoreDraftAsync();
        }
    }

    public override void Leave()
    {
        photoRevision++;
        _ = PersistDraftAsync();
        viewLifetime?.Cancel();
        onboarding.CancelPending();
    }

    private async Task RestoreDraftAsync()
    {
        restoring.Value = true;
        restoreError.Value = null;
        try
        {
            await onboarding.RestoreDraftAsync(ViewToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            restoreError.Value = Loc.RequestFailed;
        }
        finally
        {
            restoring.Value = false;
        }
    }

    private async Task PersistDraftAsync()
    {
        if (onboarding.Busy)
            return;
        try
        {
            await onboarding.SaveDraftAsync(lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!disposed)
                ShowRecoveryToast(Loc.RequestFailed);
        }
    }

    private async Task ChoosePhotoAsync()
    {
        if (onboarding.Busy || pickingPhoto.Value || ui.PickPhoto is null)
            return;
        pickingPhoto.Value = true;
        var request = photoRevision;
        try
        {
            var bytes = await ui.PickPhoto(lifetime.Token);
            await dispatcher.InvokeAsync(() =>
            {
                if (!disposed && request == photoRevision && bytes is not null)
                {
                    onboarding.SetPhoto(bytes);
                    _ = PersistDraftAsync();
                }
            });
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error)
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (!disposed)
                    ShowRecoveryToast(FniErrors.UserMessage(Loc, error));
            });
        }
        finally
        {
            await dispatcher.InvokeAsync(() => pickingPhoto.Value = false);
        }
    }

    public override Element Build()
    {
        if (restoring.Value)
            return AppLoading(Loc.Loading);
        if (restoreError.Value is { } error)
            return new VStack(
                new Text(error),
                new Button(Loc.Retry, () => _ = RestoreDraftAsync())
            );
        if (presentation.Dimensions.Value is not { } dimensions)
            return SafeArea(Splash());
        var w = dimensions.Width / 375;
        var h = Math.Max(dimensions.Height, 700) / 812;
        var r = Math.Min(w, h);
        Element Gap(float value) => new VStack().Height(value * h);
        Text Label(string value, float size = 16) =>
            new Text(value)
                .TextStyle(TextStyleFor(size == 32 ? "Bold" : "Regular"))
                .FontSize(size * w)
                .ForegroundColor(Ink);
        TextField Field(
            string value,
            Action<string> set,
            string label,
            string key,
            Action<string>? change = null,
            bool multiline = false
        ) =>
            new TextField(
                value,
                text =>
                {
                    if (onboarding.Busy)
                        return;
                    set(text);
                    change?.Invoke(text);
                    _ = PersistDraftAsync();
                }
            )
            {
                Placeholder = FieldLabel(label),
                Input = new()
                {
                    PlaceholderColor = AuthHint,
                    IsMultiline = multiline,
                    Keyboard = label == "phoneNumber" ? TextKeyboard.Telephone : TextKeyboard.Text,
                    Autocorrect = label != "username",
                    Capitalization = label is "firstName" or "lastName"
                        ? TextCapitalization.Sentences
                        : TextCapitalization.None,
                },
            }
                .TextStyle(TextStyleFor())
                .FontSize(16 * w)
                .ForegroundColor(Ink)
                .Height((multiline ? 128 : 56) * h)
                .Padding(16 * w)
                .BackgroundColor(theme.Surface)
                .CornerRadius(16 * r)
                .OnState(InteractionState.Focused, new(BorderWidth: 1, BorderColor: Accent))
                .Enabled(!onboarding.Busy)
                .WithKey(key);
        Element Gender(string value, string caption) =>
            new VStack(
                new HStack(
                    new Text(onboarding.Gender == value ? "\ue503" : "\ue504")
                        .TextStyle(TextStyleFor("Icons"))
                        .FontSize(24 * w)
                        .ForegroundColor(onboarding.Gender == value ? Accent : AuthHint)
                        .TextAlignment(TextAlignment.Center)
                        .Width(24 * w)
                        .Height(48 * h),
                    Label(caption)
                )
                    .Spacing(8 * w)
                    .CrossAlignment(CrossAxisAlignment.Center),
                new Button(
                    "",
                    () =>
                    {
                        if (onboarding.Busy)
                            return;
                        onboarding.Gender = value;
                        _ = PersistDraftAsync();
                    }
                ).Enabled(!onboarding.Busy).Position(PositionMode.Absolute).Insets(new(0, 0, 0, 0))
            )
                .Grow()
                .Shrink()
                .FlexBasis(0)
                .Height(48 * h);
        var ageValid = onboarding.IsAgeValid;
        var canSave = onboarding.CanSave;
        var photo = new VStack(
            new VStack(
                onboarding.Photo is { } image
                    ? new Image(image).Fit(ImageFit.Cover).Width(74 * w).Height(74 * h)
                    : new VStack()
            )
                .Width(74 * w)
                .Height(74 * h)
                .BackgroundColor(new Color(55, 88, 249))
                .CornerRadius(14 * r)
                .ClipToBounds(true)
                .Margin(8 * r),
            new VStack(
                new Image(FniAssets.Image("icons/edit.png"))
                    .Width(14 * w)
                    .Height(14 * h)
                    .TintColor(
                        theme.Mode == ThemeMode.Dark
                            ? new Color(38, 38, 38)
                            : new Color(217, 217, 217)
                    ),
                new Button("", () => _ = ChoosePhotoAsync())
                    .Enabled(ui.PickPhoto is not null && !pickingPhoto.Value && !onboarding.Busy)
                    .Position(PositionMode.Absolute)
                    .Insets(new(0, 0, 0, 0))
            )
                .Width(32 * w)
                .Height(32 * h)
                .CornerRadius(7 * r)
                .BackgroundColor(
                    theme.Mode == ThemeMode.Dark ? new Color(217, 217, 217) : new Color(38, 38, 38)
                )
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .Position(PositionMode.Absolute)
                .Right(0)
                .Bottom(0)
        )
            .Width(74 * w + 16 * r)
            .Height(74 * h + 16 * r);
        var birthField = new VStack(
            Label(onboarding.BirthDate?.ToString("dd.MM.yyyy") ?? Loc.DateOfBirth)
                .ForegroundColor(onboarding.BirthDate is null ? AuthHint : Ink)
                .Padding(16 * w),
            new Button("\U000f06bb", () => _ = ChooseBirthDateAsync())
                .TextStyle(TextStyleFor("Icons"))
                .FontSize(24 * r)
                .TextAlignment(TextAlignment.Center)
                .ForegroundColor(theme.OnPrimary)
                .BackgroundColor(new Color(3, 3, 3))
                .CornerRadius(16 * r)
                .Width(48 * w)
                .Height(48 * h)
                .Position(PositionMode.Absolute)
                .Right(4 * w)
                .Top(4 * h)
        )
            .Height(56 * h)
            .BackgroundColor(theme.Surface)
            .CornerRadius(16 * r);
        var location = new HStack(
            new Text("\uf193").TextStyle(TextStyleFor("Icons")).FontSize(24).ForegroundColor(Ink),
            new TextField(
                onboarding.Address,
                value =>
                {
                    if (onboarding.Busy)
                        return;
                    onboarding.Address = value;
                    _ = PersistDraftAsync();
                    _ = onboarding.SearchPlacesAsync(ViewToken);
                }
            )
            {
                Placeholder = Loc.EnterLocation,
                Input = new() { PlaceholderColor = AuthHint },
            }
                .TextStyle(TextStyleFor())
                .FontSize(16 * w)
                .ForegroundColor(Ink)
                .Grow()
                .Shrink()
                .FlexBasis(0)
                .MinWidth(0)
                .Height(56 * h)
                .Enabled(!onboarding.Busy)
                .WithKey("onboarding-address")
        ).Spacing(8 * w).Padding(16 * w, 0).Margin(4).Height(56 * h).BackgroundColor(theme.Mode == ThemeMode.Dark ? new Color(22, 22, 22) : theme.Surface).CornerRadius(16 * r).CrossAlignment(CrossAxisAlignment.Center);
        var nickname = new VStack(
            Field(
                    onboarding.Nickname,
                    value => onboarding.Nickname = value,
                    "username",
                    "onboarding-nickname",
                    text =>
                    {
                        _ = onboarding.CheckNicknameAsync(lifetime.Token);
                    }
                )
                .Border(1, new Color(246, 246, 246)),
            onboarding.NicknameChecking
                ? new VStack(BusyIndicator(Accent))
                    .Position(PositionMode.Absolute)
                    .Right(16 * w)
                    .Top(16 * h)
                : new Text(
                    onboarding.NicknameAvailable ? "\ue156"
                    : onboarding.NicknameExists ? "\ue16a"
                    : ""
                ).TextStyle(TextStyleFor("Icons")).FontSize(16 * w).ForegroundColor(onboarding.NicknameAvailable ? theme.Success : AuthWarning).Position(PositionMode.Absolute).Right(16 * w).Top(16 * h)
        ).Height(56 * h);
        return SafeArea(new VStack(new VStack().Height(56), new VStack(new ScrollView(new VStack(
                            onboarding.ValidationError is { } validation
                                ? new Text(validation).ForegroundColor(AuthWarning)
                                : new VStack(),
                            Label(Loc.UserInfo, 32)
                                .FontWeight(FontWeight.Bold)
                                .TextAlignment(TextAlignment.Center),
                            Gap(8),
                            Label(Loc.UserInfoBody, 14)
                                .ForegroundColor(new Color(128, 128, 128))
                                .TextAlignment(TextAlignment.Left),
                            Gap(24),
                            photo,
                            Gap(16),
                            Field(
                                onboarding.FirstName,
                                value => onboarding.FirstName = value,
                                "firstName",
                                "onboarding-firstname"
                            ),
                            Gap(16),
                            Field(
                                onboarding.LastName,
                                value => onboarding.LastName = value,
                                "lastName",
                                "onboarding-lastname"
                            ),
                            Gap(16),
                            nickname,
                            onboarding.Nickname.Length > 0
                            && onboarding.NicknameProblem is { } issue
                                ? Label(
                                        issue switch
                                        {
                                            NicknameIssue.Required => Loc.UsernameRequired,
                                            NicknameIssue.TooShort => Loc.UsernameValidation,
                                            NicknameIssue.InvalidFormat =>
                                                Loc.InvalidUsernameFormat,
                                            _ => throw new ArgumentOutOfRangeException(
                                                nameof(issue),
                                                issue,
                                                null
                                            ),
                                        },
                                        12
                                    )
                                    .ForegroundColor(AuthWarning)
                                : new VStack(),
                            Gap(16),
                            birthField,
                            ageValid
                                ? new VStack()
                                : Label(Loc.MustBeEighteenYearsOld, 12)
                                    .ForegroundColor(AuthWarning),
                            Gap(16),
                            Field(
                                onboarding.Phone,
                                value => onboarding.Phone = value,
                                "phoneNumber",
                                "onboarding-phone"
                            ),
                            Gap(16),
                            location,
                            new VStack(
                                onboarding
                                    .PlaceSuggestions.Select(place =>
                                        (Element)
                                            new Button(
                                                place.Description,
                                                () => _ = SelectPlaceAsync(place)
                                            )
                                                .TextStyle(TextStyleFor())
                                                .FontSize(14 * r)
                                                .ForegroundColor(Ink)
                                                .Padding(10 * r)
                                                .BackgroundColor(theme.Surface)
                                    )
                                    .ToArray()
                            ),
                            Gap(16),
                            new HStack(
                                Gender("male", Loc.Man),
                                Gender("female", Loc.Woman)
                            ).Spacing(24 * w),
                            Gap(16),
                            Field(
                                onboarding.Bio,
                                value => onboarding.Bio = value,
                                "biography",
                                "onboarding-bio",
                                multiline: true
                            ),
                            Gap(32)
                        ).Width(dimensions.Width - 32 * r).Padding(16 * w, 16 * h)).Grow().Shrink(), new VStack(new Button(onboarding.Busy ? "" : Loc.Save, () => _ = SaveProfileAsync()).TextStyle(TextStyleFor("Medium")).FontWeight(FontWeight.Medium).FontSize(16 * w).TextAlignment(TextAlignment.Center).ForegroundColor(theme.OnPrimary).Enabled(canSave && !onboarding.Busy).Height(56 * h).WidthPercent(100).BackgroundColor(canSave || onboarding.Busy ? Accent : (theme.Primary with { A = 51 })).CornerRadius(16 * r), onboarding.Busy ? new VStack(BusyIndicator()).MainAlignment(MainAxisAlignment.Center).CrossAlignment(CrossAxisAlignment.Center).Position(PositionMode.Absolute).Insets(new(0, 0, 0, 0)) : new VStack()).Height(56 * h).Width(dimensions.Width - 32 * r - 32 * w).Margin(16 * w, 0)).Grow().Shrink().FlexBasis(0).MinHeight(0).Padding(16 * r)).HeightPercent(100).BackgroundColor(theme.Background));
    }

    private static uint Argb(Color c) =>
        ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private string FieldLabel(string field) =>
        field switch
        {
            "enterLocation" => Loc.EnterLocation,
            "username" => Loc.Username,
            "firstName" => Loc.FirstName,
            "lastName" => Loc.LastName,
            "phoneNumber" => Loc.PhoneNumber,
            "biography" => Loc.Biography,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    private async Task ChooseBirthDateAsync()
    {
        if (onboarding.Busy)
            return;
        try
        {
            var result = await ui.ShowDatePickerAsync(
                new DatePickerOptions
                {
                    Renderer = DatePickerRenderer.SharedWheel,
                    InitialDate = onboarding.BirthDate is { } birth
                        ? DateOnly.FromDateTime(birth)
                        : null,
                    BackgroundColor = Argb(
                        theme.Mode == ThemeMode.Dark ? new Color(22, 22, 22) : theme.Surface
                    ),
                    TextColor = Argb(Ink),
                    Locale = presentation.Locale.Value switch
                    {
                        "sr" => "sr-Latn-RS",
                        "ru" => "ru-RU",
                        _ => "en-GB",
                    },
                    MinimumDate = new(1930, 1, 1),
                    MaximumDate = DateOnly.FromDateTime(DateTime.Today),
                    Title = Loc.ChooseDate,
                    ConfirmText = Loc.Save,
                    CancelText = Loc.Cancel,
                    Looping = true,
                    FontFamily = theme.Text.Body.Font?.Family ?? "",
                    TitleFontFamily = theme.Text.Title.Font?.Family ?? "",
                    ActionFontFamily = theme.Text.Body.Font?.Family ?? "",
                    SafeArea = new EdgeInsets(0, Insets.Top, 0, Insets.Bottom),
                    FontData = theme.Text.Body.Font?.ToArray(),
                    TitleFontData = theme.Text.Title.Font?.ToArray(),
                    ActionFontData = theme.Text.Label.Font?.ToArray(),
                    ItemColor = Argb(Ink),
                    TitleFontSize = 24 * (presentation.Dimensions.Value?.Width ?? 375) / 375,
                    ActionFontSize = 14 * (presentation.Dimensions.Value?.Width ?? 375) / 375,
                    FontSize =
                        16
                        * Math.Min(
                            (presentation.Dimensions.Value?.Width ?? 375) / 375,
                            Math.Max(presentation.Dimensions.Value?.Height ?? 812, 700) / 812
                        ),
                },
                ViewToken
            );
            if (
                result is { } date
                && !disposed
                && !ViewToken.IsCancellationRequested
                && !onboarding.Busy
            )
            {
                onboarding.BirthDate = date.ToDateTime(TimeOnly.MinValue);
                _ = PersistDraftAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!disposed)
                ShowRecoveryToast(error.Message);
        }
    }

    private async Task SelectPlaceAsync(PlaceSuggestion place)
    {
        if (onboarding.Busy)
            return;
        try
        {
            await onboarding.SelectPlaceAsync(place, ViewToken);
            await PersistDraftAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name);
            if (!disposed)
                ShowRecoveryToast(FniErrors.UserMessage(presentation.Text, error));
        }
    }

    private async Task SaveProfileAsync()
    {
        if (pickingPhoto.Value)
            return;
        try
        {
            if (session.State is not SessionState.Onboarding pending)
                return;
            if (await onboarding.SaveAsync(ViewToken) is { } result && !disposed)
                session.CompleteOnboarding(pending.Identity, result);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name);
            if (!disposed)
                form.RegistrationError =
                    error is OnboardingCreationUncertainException
                        ? error.Message
                        : FniErrors.UserMessage(presentation.Text, error);
        }
    }
}
