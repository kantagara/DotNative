using DotNative;

namespace FaliNamIgrac;

public enum PlatformActionLabel
{
    Map,
    Share,
    Retry,
    OpenExternally,
    Loading,
    ChooseMap,
    Cancel,
    InvalidInput,
    Unavailable,
    PermissionDenied,
    Error,
    Cancelled,
}

public interface IPlatformActionsText
{
    string Label(PlatformActionLabel label);
}

/// <summary>Reusable section for details and document screens; the owner disposes it on unmount.</summary>
public sealed class PlatformActionsPanel(
    UiContext ui,
    PlatformActionsNotifier actions,
    IPlatformActionsText text,
    string? appointmentId = null
) : ScreenComponent(ui)
{
    public override void Leave() => actions.Dispose();

    public override Element Build()
    {
        var state = actions.State;
        var content = new List<Element>();
        Element Button(string title, Action action) =>
            new Button(title, action)
                .TextStyle(TextStyleFor())
                .ForegroundColor(Accent)
                .Height(48)
                .Enabled(state.Status != PlatformActionStatus.Loading);
        if (appointmentId is not null)
        {
            content.Add(
                Button(
                    text.Label(PlatformActionLabel.Map),
                    () => _ = actions.OpenMapAsync(appointmentId)
                )
            );
            content.Add(
                Button(
                    text.Label(PlatformActionLabel.Share),
                    () => _ = actions.ShareGameAsync(appointmentId)
                )
            );
        }
        var label = state.Status switch
        {
            PlatformActionStatus.Loading => PlatformActionLabel.Loading,
            PlatformActionStatus.ChooseMap => PlatformActionLabel.ChooseMap,
            PlatformActionStatus.InvalidInput => PlatformActionLabel.InvalidInput,
            PlatformActionStatus.Unavailable => PlatformActionLabel.Unavailable,
            PlatformActionStatus.PermissionDenied => PlatformActionLabel.PermissionDenied,
            PlatformActionStatus.Error => PlatformActionLabel.Error,
            PlatformActionStatus.Cancelled => PlatformActionLabel.Cancelled,
            _ => (PlatformActionLabel?)null,
        };
        if (label is { } value)
            content.Add(new Text(text.Label(value)).TextStyle(TextStyleFor()).ForegroundColor(Ink));
        if (state.Status == PlatformActionStatus.ChooseMap)
        {
            foreach (var map in state.Maps ?? [])
                content.Add(
                    Button(
                        map == AppointmentMap.Apple ? "Apple Maps" : "Google Maps",
                        () => _ = actions.SelectMapAsync(map)
                    )
                );
            content.Add(Button(text.Label(PlatformActionLabel.Cancel), actions.Invalidate));
        }
        if (state.CanOpenExternally)
            content.Add(
                Button(
                    text.Label(PlatformActionLabel.OpenExternally),
                    () => _ = actions.OpenDocumentExternallyAsync()
                )
            );
        if (
            state.Status
            is PlatformActionStatus.Error
                or PlatformActionStatus.Unavailable
                or PlatformActionStatus.Cancelled
                or PlatformActionStatus.PermissionDenied
        )
            content.Add(
                Button(text.Label(PlatformActionLabel.Retry), () => _ = actions.RetryAsync())
            );
        return new VStack(content.ToArray()).Spacing(8);
    }
}
