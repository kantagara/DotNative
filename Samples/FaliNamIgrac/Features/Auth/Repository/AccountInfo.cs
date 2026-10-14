namespace FaliNamIgrac;

public sealed record AccountInfo(
    string? Email = null,
    string? UserId = null,
    string? GivenName = null,
    string? FamilyName = null,
    string? Identities = null
)
{
    internal static AccountInfo FromCognito(IReadOnlyDictionary<string, string> attributes) =>
        new(
            attributes.GetValueOrDefault("email"),
            attributes.GetValueOrDefault("sub"),
            attributes.GetValueOrDefault("given_name"),
            attributes.GetValueOrDefault("family_name"),
            attributes.GetValueOrDefault("identities")
        );
}
