namespace FaliNamIgrac;

internal sealed class GameEditorLocalization(UiContext ui) : IGameEditorText
{
    public string Label(GameEditorLabel value) =>
        value switch
        {
            GameEditorLabel.Create => ui.Text.GameEditorLabelCreate,
            GameEditorLabel.Edit => ui.Text.GameEditorLabelEdit,
            GameEditorLabel.Sport => ui.Text.GameEditorLabelSport,
            GameEditorLabel.Location => ui.Text.GameEditorLabelLocation,
            GameEditorLabel.Date => ui.Text.GameEditorLabelDate,
            GameEditorLabel.Start => ui.Text.GameEditorLabelStart,
            GameEditorLabel.End => ui.Text.GameEditorLabelEnd,
            GameEditorLabel.Needed => ui.Text.GameEditorLabelNeeded,
            GameEditorLabel.Total => ui.Text.GameEditorLabelTotal,
            GameEditorLabel.Price => ui.Text.GameEditorLabelPrice,
            GameEditorLabel.Currency => ui.Text.GameEditorLabelCurrency,
            GameEditorLabel.Description => ui.Text.GameEditorLabelDescription,
            GameEditorLabel.Terrain => ui.Text.GameEditorLabelTerrain,
            GameEditorLabel.Unspecified => ui.Text.GameEditorLabelUnspecified,
            GameEditorLabel.Indoor => ui.Text.GameEditorLabelIndoor,
            GameEditorLabel.Outdoor => ui.Text.GameEditorLabelOutdoor,
            GameEditorLabel.Grass => ui.Text.GameEditorLabelGrass,
            GameEditorLabel.Concrete => ui.Text.GameEditorLabelConcrete,
            GameEditorLabel.Parquet => ui.Text.GameEditorLabelParquet,
            GameEditorLabel.Clinker => ui.Text.GameEditorLabelClinker,
            GameEditorLabel.Sand => ui.Text.GameEditorLabelSand,
            GameEditorLabel.Save => ui.Text.GameEditorLabelSave,
            GameEditorLabel.Cancel => ui.Text.GameEditorLabelCancel,
            GameEditorLabel.Retry => ui.Text.GameEditorLabelRetry,
            GameEditorLabel.Loading => ui.Text.GameEditorLabelLoading,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    public string Error(GameEditorIssue value) =>
        value switch
        {
            GameEditorIssue.Sport => ui.Text.GameEditorErrorSport,
            GameEditorIssue.Location => ui.Text.GameEditorErrorLocation,
            GameEditorIssue.Date => ui.Text.GameEditorErrorDate,
            GameEditorIssue.Time => ui.Text.GameEditorErrorTime,
            GameEditorIssue.Capacity => ui.Text.GameEditorErrorCapacity,
            GameEditorIssue.Price => ui.Text.GameEditorErrorPrice,
            GameEditorIssue.Currency => ui.Text.GameEditorErrorCurrency,
            GameEditorIssue.Terrain => ui.Text.GameEditorErrorTerrain,
            GameEditorIssue.Venue => ui.Text.GameEditorErrorVenue,
            GameEditorIssue.Limits => ui.Text.GameEditorErrorLimits,
            GameEditorIssue.Unauthorized => ui.Text.GameEditorErrorUnauthorized,
            GameEditorIssue.Rejected => ui.Text.GameEditorErrorRejected,
            GameEditorIssue.Server => ui.Text.GameEditorErrorServer,
            GameEditorIssue.Network => ui.Text.GameEditorErrorNetwork,
            GameEditorIssue.InvalidResponse => ui.Text.GameEditorErrorInvalidResponse,
            GameEditorIssue.Session => ui.Text.GameEditorErrorSession,
            GameEditorIssue.Picker => ui.Text.GameEditorErrorPicker,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
}

internal sealed class AppointmentLocalization(UiContext ui) : IAppointmentText
{
    public string Action(AppointmentAction value) =>
        value switch
        {
            AppointmentAction.Apply => ui.Text.AppointmentApply,
            AppointmentAction.Withdraw => ui.Text.AppointmentWithdraw,
            AppointmentAction.Accept => ui.Text.AppointmentAccept,
            AppointmentAction.Reject => ui.Text.AppointmentReject,
            AppointmentAction.Remove => ui.Text.AppointmentRemove,
            AppointmentAction.Ban => ui.Text.AppointmentBan,
            AppointmentAction.Delete => ui.Text.AppointmentDelete,
            AppointmentAction.Rate => ui.Text.AppointmentRate,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    public string Status(AppointmentRole value) =>
        value switch
        {
            AppointmentRole.Visitor => ui.Text.AppointmentVisitor,
            AppointmentRole.Pending => ui.Text.AppointmentPending,
            AppointmentRole.Accepted => ui.Text.AppointmentAccepted,
            AppointmentRole.Rejected => ui.Text.AppointmentRejected,
            AppointmentRole.Organizer => ui.Text.AppointmentOrganizer,
            AppointmentRole.Unknown => ui.Text.AppointmentUnknown,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    public string Confirm(AppointmentAction value) =>
        value switch
        {
            AppointmentAction.Withdraw => ui.Text.AppointmentConfirmWithdraw,
            AppointmentAction.Reject => ui.Text.AppointmentConfirmReject,
            AppointmentAction.Remove => ui.Text.AppointmentConfirmRemove,
            AppointmentAction.Ban => ui.Text.AppointmentConfirmBan,
            AppointmentAction.Delete => ui.Text.AppointmentConfirmDelete,
            AppointmentAction.Rate => ui.Text.AppointmentConfirmRate,
            _ => ui.Text.AppointmentConfirm,
        };

    public string Loading => ui.Text.AppointmentLoading;
    public string Retry => ui.Text.AppointmentRetry;
    public string Cancel => ui.Text.AppointmentCancel;
    public string Edit => ui.Text.AppointmentEdit;
    public string Chat => ui.Text.AppointmentChat;
    public string Deleted => ui.Text.AppointmentDeleted;
    public string Rated => ui.Text.AppointmentRated;
    public string Report => ui.Text.Reporting08ReportTitle;
    public string ConfirmButton => ui.Text.AppointmentConfirm;

    public string Error(AppointmentProblem value) =>
        value switch
        {
            AppointmentProblem.Load => ui.Text.AppointmentLoadError,
            AppointmentProblem.Mutation => ui.Text.AppointmentMutationError,
            AppointmentProblem.Refresh => ui.Text.AppointmentRefreshError,
            AppointmentProblem.InvalidAction => ui.Text.AppointmentInvalidAction,
            _ => "",
        };
}

internal sealed class ReportingLocalization(UiContext ui) : IReportingText
{
    public string Label(ReportingLabel value) =>
        value switch
        {
            ReportingLabel.ReportTitle => ui.Text.Reporting08ReportTitle,
            ReportingLabel.Reason => ui.Text.Reporting08Reason,
            ReportingLabel.Context => ui.Text.Reporting08Context,
            ReportingLabel.Send => ui.Text.Reporting08Send,
            ReportingLabel.Sent => ui.Text.Reporting08Sent,
            ReportingLabel.Cancel => ui.Text.Reporting08Cancel,
            ReportingLabel.Retry => ui.Text.Reporting08Retry,
            ReportingLabel.Loading => ui.Text.Reporting08Loading,
            ReportingLabel.BannedTitle => ui.Text.Reporting08BannedTitle,
            ReportingLabel.TemporaryBan => ui.Text.Reporting08TemporaryBan,
            ReportingLabel.PermanentBan => ui.Text.Reporting08PermanentBan,
            ReportingLabel.UnknownBan => ui.Text.Reporting08UnknownBan,
            ReportingLabel.Until => ui.Text.Reporting08Until,
            ReportingLabel.SignOut => ui.Text.Reporting08SignOut,
            ReportingLabel.ReportsTitle => ui.Text.Reporting08ReportsTitle,
            ReportingLabel.Empty => ui.Text.Reporting08Empty,
            ReportingLabel.Reporter => ui.Text.Reporting08Reporter,
            ReportingLabel.Reported => ui.Text.Reporting08Reported,
            ReportingLabel.Pending => ui.Text.Reporting08Pending,
            ReportingLabel.Approved => ui.Text.Reporting08Approved,
            ReportingLabel.Dismissed => ui.Text.Reporting08Dismissed,
            ReportingLabel.Revoked => ui.Text.Reporting08Revoked,
            ReportingLabel.UnknownStatus => ui.Text.Reporting08UnknownStatus,
            ReportingLabel.Ban7 => ui.Text.Reporting08Ban7,
            ReportingLabel.Ban30 => ui.Text.Reporting08Ban30,
            ReportingLabel.Ban365 => ui.Text.Reporting08Ban365,
            ReportingLabel.BanPermanent => ui.Text.Reporting08BanPermanent,
            ReportingLabel.Dismiss => ui.Text.Reporting08Dismiss,
            ReportingLabel.Revoke => ui.Text.Reporting08Revoke,
            ReportingLabel.Unban => ui.Text.Reporting08Unban,
            ReportingLabel.Confirm => ui.Text.Reporting08Confirm,
            ReportingLabel.ConfirmDecision => ui.Text.Reporting08ConfirmDecision,
            ReportingLabel.Applied => ui.Text.Reporting08Applied,
            ReportingLabel.NoActiveBan => ui.Text.Reporting08NoActiveBan,
            ReportingLabel.Refresh => ui.Text.Reporting08Refresh,
            ReportingLabel.CheckFailed => ui.Text.Reporting08CheckFailed,
            ReportingLabel.Back => ui.Text.Reporting08Back,
            ReportingLabel.PlayerProfile => ui.Text.Reporting08PlayerProfile,
            ReportingLabel.Chat => ui.Text.Reporting08Chat,
            ReportingLabel.GameDetails => ui.Text.Reporting08GameDetails,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    public string Error(ReportingIssue value) =>
        value switch
        {
            ReportingIssue.InvalidTarget => ui.Text.Reporting08ErrorInvalidTarget,
            ReportingIssue.InvalidReason => ui.Text.Reporting08ErrorInvalidReason,
            ReportingIssue.Unauthorized => ui.Text.Reporting08ErrorUnauthorized,
            ReportingIssue.Forbidden => ui.Text.Reporting08ErrorForbidden,
            ReportingIssue.AlreadyReported => ui.Text.Reporting08ErrorAlreadyReported,
            ReportingIssue.RateLimited => ui.Text.Reporting08ErrorRateLimited,
            ReportingIssue.NotFound => ui.Text.Reporting08ErrorNotFound,
            ReportingIssue.Network => ui.Text.Reporting08ErrorNetwork,
            ReportingIssue.InvalidResponse => ui.Text.Reporting08ErrorInvalidResponse,
            ReportingIssue.Banned => ui.Text.Reporting08ErrorBanned,
            ReportingIssue.InvalidDecision => ui.Text.Reporting08ErrorInvalidDecision,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
}

internal sealed class PlatformActionsLocalization(UiContext ui) : IPlatformActionsText
{
    public string Label(PlatformActionLabel value) =>
        value switch
        {
            PlatformActionLabel.Map => ui.Text.PlatformActionsMap,
            PlatformActionLabel.Share => ui.Text.PlatformActionsShare,
            PlatformActionLabel.Retry => ui.Text.PlatformActionsRetry,
            PlatformActionLabel.OpenExternally => ui.Text.PlatformActionsOpenExternally,
            PlatformActionLabel.Loading => ui.Text.PlatformActionsLoading,
            PlatformActionLabel.ChooseMap => ui.Text.PlatformActionsChooseMap,
            PlatformActionLabel.Cancel => ui.Text.PlatformActionsCancel,
            PlatformActionLabel.InvalidInput => ui.Text.PlatformActionsInvalidInput,
            PlatformActionLabel.Unavailable => ui.Text.PlatformActionsUnavailable,
            PlatformActionLabel.PermissionDenied => ui.Text.PlatformActionsPermissionDenied,
            PlatformActionLabel.Error => ui.Text.PlatformActionsError,
            PlatformActionLabel.Cancelled => ui.Text.PlatformActionsCancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
}

internal sealed class AdPlacementLocalization(UiContext ui) : IAdPlacementText
{
    public string Label(AdPlacementLabel value) =>
        value switch
        {
            AdPlacementLabel.Sponsored => ui.Text.AdPlacementSponsored,
            AdPlacementLabel.OpenSponsor => ui.Text.AdPlacementOpenSponsor,
            AdPlacementLabel.Loading => ui.Text.AdPlacementLoading,
            AdPlacementLabel.Retry => ui.Text.AdPlacementRetry,
            AdPlacementLabel.Error => ui.Text.AdPlacementError,
            AdPlacementLabel.Unavailable => ui.Text.AdPlacementUnavailable,
            AdPlacementLabel.ConsentDenied => ui.Text.AdPlacementConsentDenied,
            AdPlacementLabel.PrivacyOptions => ui.Text.AdPlacementPrivacyOptions,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
}
