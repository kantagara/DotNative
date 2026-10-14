using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaliNamIgrac;

public enum CatalogOrder
{
    Closest,
    PriceAscending,
    PriceDescending,
    TimeAscending,
    TimeDescending,
}

public sealed record CatalogFilters(
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    decimal DistanceKm = 100
)
{
    public void Validate()
    {
        if (MinPrice < 0 || MaxPrice < 0 || MinPrice > MaxPrice || DistanceKm is < 0 or > 200)
            throw new ArgumentException("Invalid filter range.");
    }
}

public sealed record CatalogSearchQuery(
    int Sport = 0,
    int Page = 1,
    int Limit = 30,
    CatalogOrder Order = CatalogOrder.Closest,
    CatalogFilters? Filters = null
)
{
    public CatalogFilters EffectiveFilters => Filters ?? new();

    public CatalogSearchQuery FirstPage() => this with { Page = 1 };

    public string Path(PlayerProfile user)
    {
        Validate();
        if (
            string.IsNullOrWhiteSpace(user.Id)
            || !double.IsFinite(user.Latitude)
            || !double.IsFinite(user.Longitude)
            || Math.Abs(user.Latitude) > 90
            || Math.Abs(user.Longitude) > 180
            || user.Age < 0
        )
            throw new ArgumentException("Invalid search profile.");
        var filters = EffectiveFilters;
        var payload = new SearchFilterPayload(
            filters.MinPrice,
            filters.MaxPrice,
            Order == CatalogOrder.Closest ? true : null,
            Order == CatalogOrder.PriceAscending ? "ASC"
                : Order == CatalogOrder.PriceDescending ? "DESC"
                : null,
            Order == CatalogOrder.TimeDescending ? "DESC" : "ASC"
        );
        var fields = new List<KeyValuePair<string, string>>
        {
            new("userId", user.Id),
            new("userLat", user.Latitude.ToString("R", CultureInfo.InvariantCulture)),
            new("userLong", user.Longitude.ToString("R", CultureInfo.InvariantCulture)),
            new("age", N(user.Age)),
            new("page", N(Page)),
            new("limit", N(Limit)),
            new("distance", N(filters.DistanceKm * 1000)),
            new(
                "filters",
                JsonSerializer.Serialize(payload, FilteringJson.Default.SearchFilterPayload)
            ),
        };
        if (Sport != 0)
            fields.Add(new("sportId", N(Sport)));
        return "game-data/games?"
            + string.Join(
                "&",
                fields.Select(pair =>
                    Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)
                )
            );
    }

    public void Validate()
    {
        if (Sport != 0 && !FniSports.All.Any(s => s.Id == Sport))
            throw new ArgumentOutOfRangeException(nameof(Sport));
        if (Page is < 1 or > 10000 || Limit is < 1 or > 200 || !Enum.IsDefined(Order))
            throw new ArgumentOutOfRangeException(nameof(Page));
        EffectiveFilters.Validate();
    }

    private static string N(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}

internal sealed record SearchFilterPayload(
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? SortByDistance,
    string? SortByPrice,
    string SortByTime
);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(SearchFilterPayload))]
[JsonSerializable(typeof(GameResponse[]))]
internal partial class FilteringJson : JsonSerializerContext { }

public sealed record CatalogFilterDraft(
    string MinPrice = "",
    string MaxPrice = "",
    string DistanceKm = "100"
)
{
    public static CatalogFilterDraft From(CatalogFilters filters) =>
        new(
            filters.MinPrice?.ToString(CultureInfo.InvariantCulture) ?? "",
            filters.MaxPrice?.ToString(CultureInfo.InvariantCulture) ?? "",
            filters.DistanceKm.ToString(CultureInfo.InvariantCulture)
        );

    public bool TryApply(out CatalogFilters filters)
    {
        filters = new();
        if (
            !Number(MinPrice, out var min)
            || !Number(MaxPrice, out var max)
            || !Number(DistanceKm, out var distance)
            || distance is null
        )
            return false;
        filters = new(min, max, distance.Value);
        try
        {
            filters.Validate();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool Number(string text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (
            !decimal.TryParse(
                text.Trim().Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var number
            )
        )
            return false;
        value = number;
        return true;
    }
}
