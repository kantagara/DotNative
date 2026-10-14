using DotNative.Cognito;

namespace FaliNamIgrac;

/// <summary>
/// Google and Apple through Cognito hosted UI. The browser session, PKCE and
/// token exchange live in DotNative.Cognito. This app stores the session only
/// after those tokens arrive.
/// </summary>
public sealed class SocialAuthentication(ICognitoHostedUi hosted)
{
    public async Task<AccountInfo> SignInAsync(
        ISessionService session,
        string provider,
        CancellationToken token
    )
    {
        var cognito = provider switch
        {
            "Google" => CognitoProvider.Google,
            "SignInWithApple" => CognitoProvider.Apple,
            _ => throw new ArgumentException("Unsupported sign-in provider.", nameof(provider)),
        };
        var tokens = await hosted.SignInAsync(cognito, token);
        token.ThrowIfCancellationRequested();
        return await session.AcceptWebSessionAsync(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresIn,
            tokens.IdToken,
            token
        );
    }
}
