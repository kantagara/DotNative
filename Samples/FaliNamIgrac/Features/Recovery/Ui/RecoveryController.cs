using System.Text.RegularExpressions;
using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed class RecoveryController(
    ISessionService session,
    IUiDispatcher dispatcher,
    IRecoveryProgressStore? progress = null
) : Notifier<RecoveryStep>(dispatcher, RecoveryStep.Initial), IRecoveryController
{
    private readonly State<string?> email = new(null);
    private readonly State<string> code = new("");
    private readonly State<string> password = new("");
    private readonly State<string> confirmation = new("");
    private readonly State<bool> passwordVisible = new(false);
    private readonly State<bool> confirmationVisible = new(false);
    private readonly State<bool> emailError = new(false);
    private readonly State<bool> responsePopupVisible = new(false);
    private readonly State<bool> requestSucceeded = new(false);
    private readonly State<bool> confirmationTouched = new(false);

    public string? Email
    {
        get => email.Value;
        set => email.Value = value;
    }

    public string Code
    {
        get => code.Value;
        set => code.Value = value;
    }

    public string Password
    {
        get => password.Value;
        set => password.Value = value;
    }

    public string Confirmation
    {
        get => confirmation.Value;
        set => confirmation.Value = value;
    }

    public bool PasswordVisible
    {
        get => passwordVisible.Value;
        set => passwordVisible.Value = value;
    }

    public bool ConfirmationVisible
    {
        get => confirmationVisible.Value;
        set => confirmationVisible.Value = value;
    }

    public bool EmailError
    {
        get => emailError.Value;
        set => emailError.Value = value;
    }

    public bool ResponsePopupVisible
    {
        get => responsePopupVisible.Value;
        set => responsePopupVisible.Value = value;
    }

    public bool RequestSucceeded
    {
        get => requestSucceeded.Value;
        set => requestSucceeded.Value = value;
    }

    public bool ConfirmationTouched
    {
        get => confirmationTouched.Value;
        set => confirmationTouched.Value = value;
    }

    private string resetEmail = "";
    private int revision;

    public void Initialize()
    {
        revision++;
        Email = null;
        EmailError = false;
        Code = "";
        Password = "";
        Confirmation = "";
        PasswordVisible = false;
        ConfirmationVisible = false;
        ResponsePopupVisible = false;
        RequestSucceeded = false;
        ConfirmationTouched = false;
        SetState(RecoveryStep.Initial);
        resetEmail = "";
    }

    public async Task<bool> RestoreProgressAsync(CancellationToken token)
    {
        if (progress is null || Busy)
            return false;
        var requestRevision = revision;
        var saved = await progress.ReadEmailAsync(token);
        token.ThrowIfCancellationRequested();
        if (saved is null || requestRevision != revision)
            return false;
        Email = resetEmail = saved;
        Code = Password = Confirmation = "";
        SetState(RecoveryStep.Reset);
        return true;
    }

    public bool IsEmailValid => Email is null || Regex.IsMatch(Email, "^[^@]+@[^@]+\\.[^@]+$");
    public bool CanRequestReset => IsEmailValid && !string.IsNullOrEmpty(Email);
    public bool IsConfirmationValid => Password.Length == 0 || Password == Confirmation;
    public bool CanConfirmReset => Password.Length > 0 && Password == Confirmation;
    public bool CanContinueCode => Code.Length == 6;

    public bool AdvanceToPassword()
    {
        if (State is not (RecoveryStep.Reset or RecoveryStep.Code) || !CanContinueCode)
            return false;
        SetState(RecoveryStep.Code);
        return true;
    }

    public Task RequestResetAsync(CancellationToken token) =>
        RunExclusive(() => RequestResetCoreAsync(token));

    private async Task RequestResetCoreAsync(CancellationToken token)
    {
        var requestRevision = revision;
        if (!CanRequestReset || Email is not { } address)
            return;
        var success = false;
        try
        {
            await session.RequestPasswordResetAsync(address.Trim(), token);
            if (requestRevision != revision)
                return;
            if (progress is not null)
                await progress.SaveEmailAsync(address.Trim(), token);
            success = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Password reset failed: " + error.GetType().Name);
        }
        if (token.IsCancellationRequested || requestRevision != revision)
            return;
        SetState(success ? RecoveryStep.Reset : RecoveryStep.Error);
        if (success)
            resetEmail = address.Trim();
        RequestSucceeded = success;
        ResponsePopupVisible = true;
    }

    public Task<bool> ConfirmResetAsync(CancellationToken token) =>
        RunExclusive(() => ConfirmResetCoreAsync(token));

    private async Task<bool> ConfirmResetCoreAsync(CancellationToken token)
    {
        var requestRevision = revision;
        if (State != RecoveryStep.Code || !CanConfirmReset || !CanContinueCode)
            return false;
        var success = false;
        try
        {
            await session.ConfirmPasswordResetAsync(resetEmail, Code, Password, token);
            success = true;
            if (requestRevision != revision)
                return false;
            if (progress is not null)
                await progress.SaveEmailAsync(null, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Password confirmation failed: " + error.GetType().Name);
        }
        if (token.IsCancellationRequested || requestRevision != revision)
            return false;
        SetState(success ? RecoveryStep.Confirmed : RecoveryStep.Code);
        RequestSucceeded = success;
        ResponsePopupVisible = !success;
        if (success)
        {
            Password = "";
            Confirmation = "";
        }
        return success;
    }
}

public enum RecoveryStep
{
    Initial,
    Reset,
    Code,
    Confirmed,
    Error,
}
