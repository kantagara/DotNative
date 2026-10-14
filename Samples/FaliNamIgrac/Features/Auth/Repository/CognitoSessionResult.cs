using Amazon.CognitoIdentityProvider.Model;

namespace FaliNamIgrac;

public sealed class UnsupportedAuthenticationChallengeException(string challenge)
    : InvalidOperationException(
        "This account requires an additional sign-in challenge that this app cannot yet complete."
    )
{
    public string Challenge { get; } = challenge;
}

public static class CognitoSessionResult
{
    public static AuthenticationResultType RequireComplete(
        AuthenticationResultType? result,
        string? challenge,
        bool requireRefresh = true
    )
    {
        // Fail closed for MFA, NEW_PASSWORD_REQUIRED, setup, selection and future challenges.
        if (!string.IsNullOrEmpty(challenge))
            throw new UnsupportedAuthenticationChallengeException(challenge);
        if (
            result is null
            || string.IsNullOrWhiteSpace(result.AccessToken)
            || string.IsNullOrWhiteSpace(result.IdToken)
            || result.ExpiresIn is not > 0
            || (requireRefresh && string.IsNullOrWhiteSpace(result.RefreshToken))
        )
            throw new InvalidDataException(
                "Your session could not be verified. Please sign in again."
            );
        return result;
    }
}
