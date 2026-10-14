namespace FaliNamIgrac;

// Integrator supplies translated text from ARB resources; no generated resource is edited here.
public sealed record FilterText(
    string MinPrice,
    string MaxPrice,
    string DistanceKm,
    string Apply,
    string Reset,
    string Cancel,
    string InvalidRange,
    string Unavailable,
    string DistanceHint,
    string MyGamesLimit,
    string Retry,
    string Next,
    string Previous
)
{
    public static FilterText Serbian { get; } =
        new(
            "Minimalna cena po igraču",
            "Maksimalna cena po igraču",
            "Udaljenost (km, 0–200)",
            "Primeni",
            "Resetuj",
            "Otkaži",
            "Unesi nenegativne cene, minimum ≤ maksimum i udaljenost od 0 do 200 km.",
            "Pretraga još nije povezana.",
            "Oblast pretrage je približna; backend koristi geografski okvir.",
            "Moji termini podržavaju sport i prošle/buduće termine; redosled je od najnovijeg datuma.",
            "Pokušaj ponovo",
            "Sledeća",
            "Prethodna"
        );
}
