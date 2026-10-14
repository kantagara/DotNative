using System.Text.Json;
using System.Text.Json.Serialization;
using DotNative.Firebase;
using DotNative.Preferences;

namespace FaliNamIgrac;

public enum AdPlacement
{
    Feed,
    GameDetails,
    Chat,
    AppOpen,
}

public sealed record SponsorAd(
    [property: JsonPropertyName("ad_name")] string Name,
    [property: JsonPropertyName("image_url")] string ImageUrl,
    [property: JsonPropertyName("redirect_url")] string RedirectUrl,
    [property: JsonPropertyName("campaign_start_date")] DateTimeOffset Starts,
    [property: JsonPropertyName("duration")] int DurationDays,
    [property: JsonPropertyName("appearance_per_day")] int DailyLimit,
    [property: JsonPropertyName("appearances_in_a_day")] int AppearancesInADay
);

public sealed record SponsorConfig
{
    [JsonPropertyName("interstitial")]
    public required SponsorAd[] Interstitial { get; init; }

    [JsonPropertyName("feed")]
    public required SponsorAd[] Feed { get; init; }

    [JsonPropertyName("game_details")]
    public required SponsorAd[] GameDetails { get; init; }

    [JsonPropertyName("chat")]
    public required SponsorAd[] Chat { get; init; }
    public static SponsorConfig Empty =>
        new()
        {
            Interstitial = [],
            Feed = [],
            GameDetails = [],
            Chat = [],
        };

    public SponsorAd[] For(AdPlacement placement) =>
        placement switch
        {
            AdPlacement.Feed => Feed,
            AdPlacement.GameDetails => GameDetails,
            AdPlacement.Chat => Chat,
            AdPlacement.AppOpen => Interstitial,
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };
}

public interface ISponsorConfigSource
{
    Task<SponsorConfig> FetchAsync(CancellationToken token);
}

public sealed class FirebaseSponsorConfigSource(IFirebaseRemoteConfig remote) : ISponsorConfigSource
{
    public const string Key = "InGamePartnershipAds";

    public async Task<SponsorConfig> FetchAsync(CancellationToken token)
    {
        await remote.FetchRemoteConfigAsync(token);
        token.ThrowIfCancellationRequested();
        var value = await remote.GetRemoteStringAsync(Key, token);
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(value))
            return SponsorConfig.Empty;
        if (value.Length > 1_000_000)
            throw new InvalidDataException("Sponsor configuration is too large.");
        var config =
            JsonSerializer.Deserialize(value, MonetizationJson.Default.SponsorConfig)
            ?? throw new InvalidDataException("Sponsor configuration is null.");
        if (
            config.Interstitial is null
            || config.Feed is null
            || config.GameDetails is null
            || config.Chat is null
        )
            throw new InvalidDataException("Sponsor placements are missing.");
        return config;
    }
}

public sealed record SponsorLedger(
    DateOnly Day,
    SponsorConfig? Config,
    Dictionary<string, int> Counts,
    int Foregrounds
);

public interface ISponsorLedgerStore
{
    Task<SponsorLedger?> ReadAsync(string account, CancellationToken token);
    Task WriteAsync(string account, SponsorLedger ledger, CancellationToken token);
}

public sealed class PreferencesSponsorLedgerStore(IPreferences preferences) : ISponsorLedgerStore
{
    private static string Key(string account) => "fni.sponsors.v1." + Uri.EscapeDataString(account);

    public async Task<SponsorLedger?> ReadAsync(string account, CancellationToken token)
    {
        var json = await preferences.GetAsync(Key(account), "", token);
        if (json.Length == 0)
            return null;
        // Corrupt storage is a retryable error; do not silently reset frequency caps.
        return JsonSerializer.Deserialize(json, MonetizationJson.Default.SponsorLedger)
            ?? throw new InvalidDataException("Invalid sponsor ledger.");
    }

    public Task WriteAsync(string account, SponsorLedger ledger, CancellationToken token) =>
        preferences.SetAsync(
            Key(account),
            JsonSerializer.Serialize(ledger, MonetizationJson.Default.SponsorLedger),
            token
        );
}

[JsonSerializable(typeof(SponsorConfig))]
[JsonSerializable(typeof(SponsorLedger))]
internal partial class MonetizationJson : JsonSerializerContext;
