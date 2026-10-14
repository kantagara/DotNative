namespace FaliNamIgrac;

public abstract record SessionState
{
    private SessionState() { }

    public sealed record SignedOut : SessionState;

    public sealed record Onboarding(RegistrationIdentity Identity) : SessionState;

    public sealed record SignedIn(PlayerProfile Profile) : SessionState;
}
