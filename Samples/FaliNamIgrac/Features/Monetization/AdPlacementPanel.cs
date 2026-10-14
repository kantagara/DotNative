using DotNative;

namespace FaliNamIgrac;

public enum AdPlacementLabel
{
    Sponsored,
    OpenSponsor,
    Loading,
    Retry,
    Error,
    Unavailable,
    ConsentDenied,
    PrivacyOptions,
}

public interface IAdPlacementText
{
    string Label(AdPlacementLabel label);
}

/// <summary>Inline feed/details/chat content. Mounting/viewport host must explicitly call
/// MarkSponsorVisibleAsync after actual visibility, and await SuspendAsync when hidden.
/// AppOpen sponsors require a modal host; do not mount them inline.</summary>
public sealed class AdPlacementPanel(
    UiContext ui,
    AdPlacementNotifier ads,
    PlatformActionsNotifier actions,
    IAdPlacementText text
) : ScreenComponent(ui)
{
    public Task InitializeAsync(CancellationToken token = default) => ads.LoadAsync(token);

    public Task SuspendAsync() => ads.SuspendAsync();

    public override Element Build()
    {
        var state = ads.State;
        Element Label(AdPlacementLabel label) =>
            new Text(text.Label(label)).TextStyle(TextStyleFor()).ForegroundColor(Ink);
        Element Button(AdPlacementLabel label, Action action) =>
            new Button(text.Label(label), action)
                .TextStyle(TextStyleFor())
                .ForegroundColor(Accent)
                .Height(48);
        if (
            state.Status == AdPlacementStatus.SponsorReady
            && state.Image is { } image
            && state.Sponsor is { } sponsor
        )
            return new VStack(
                Label(AdPlacementLabel.Sponsored),
                new Image(image).Fit(ImageFit.Contain).Height(250),
                Button(
                    AdPlacementLabel.OpenSponsor,
                    () => _ = actions.OpenExternalAsync(sponsor.Ad.RedirectUrl)
                )
            );
        if (state.Status == AdPlacementStatus.AdReady && state.AdView is { } view)
            return view;
        if (state.Status == AdPlacementStatus.Loading)
            return Label(AdPlacementLabel.Loading);
        if (
            state.Status
            is AdPlacementStatus.Error
                or AdPlacementStatus.Unavailable
                or AdPlacementStatus.ConsentDenied
                or AdPlacementStatus.Cancelled
        )
            return new VStack(
                Label(
                    state.Status switch
                    {
                        AdPlacementStatus.Unavailable => AdPlacementLabel.Unavailable,
                        AdPlacementStatus.ConsentDenied => AdPlacementLabel.ConsentDenied,
                        _ => AdPlacementLabel.Error,
                    }
                ),
                Button(AdPlacementLabel.Retry, () => _ = ads.RetryAsync()),
                Button(AdPlacementLabel.PrivacyOptions, () => _ = ads.ShowPrivacyOptionsAsync())
            );
        return new VStack();
    }
}
