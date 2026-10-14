using System.Text.Json.Serialization;

namespace FaliNamIgrac;

public sealed record ReportingAccount(int UserId, string AccessToken)
{
    public override string ToString() => $"ReportingAccount({UserId})";
}

public sealed record CreateUserReport(
    [property: JsonPropertyName("reportingUserId")] int ReportingUserId,
    [property: JsonPropertyName("reportedUserId")] int ReportedUserId,
    [property: JsonPropertyName("reportReason")] string ReportReason
);

public sealed record UpdateUserReport(
    [property: JsonPropertyName("report_status")] string Status,
    [property: JsonPropertyName("ban_duration_days")] int? Days = null,
    [property: JsonPropertyName("ban_start_date")] DateTimeOffset? Start = null
);

public sealed record ReportingBanStatus(
    [property: JsonPropertyName("banned"), JsonRequired] bool Banned,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("unbannedDate")] string? UnbannedDate = null,
    [property: JsonPropertyName("message")] string? Message = null
);

public sealed record ReportingAdminAccess(
    [property: JsonPropertyName("is_admin"), JsonRequired] bool IsAdmin
);

public sealed record ReportingUnbanResult(
    [property: JsonPropertyName("revoked_count"), JsonRequired] int RevokedCount
);

public sealed record ModerationReport(
    [property: JsonPropertyName("report_id"), JsonRequired] int Id,
    [property: JsonPropertyName("reporting_user_id"), JsonRequired] int ReportingUserId,
    [property: JsonPropertyName("reported_user_id"), JsonRequired] int ReportedUserId,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("report_status")] string Status,
    [property: JsonPropertyName("ban_duration_days")] int? Days = null,
    [property: JsonPropertyName("ban_start_date")] string? Start = null,
    [property: JsonPropertyName("created_at")] string? Created = null
)
{
    public DateTimeOffset? CreatedAt =>
        DateTimeOffset.TryParse(
            Created,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var date
        )
            ? date
            : null;
    public bool Pending => Status == "PENDING";
    public bool Approved => Status == "APPROVED";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateUserReport))]
[JsonSerializable(typeof(UpdateUserReport))]
[JsonSerializable(typeof(ReportingBanStatus))]
[JsonSerializable(typeof(ReportingAdminAccess))]
[JsonSerializable(typeof(ReportingUnbanResult))]
[JsonSerializable(typeof(ModerationReport[]))]
internal partial class ReportingJsonContext : JsonSerializerContext;
