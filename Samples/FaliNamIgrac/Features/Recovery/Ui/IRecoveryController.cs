using DotNative;

namespace FaliNamIgrac;

public interface IRecoveryController
{
    bool Busy { get; }
    string? Email { get; set; }
    string Code { get; set; }
    string Password { get; set; }
    string Confirmation { get; set; }
    bool PasswordVisible { get; set; }
    bool ConfirmationVisible { get; set; }
    bool EmailError { get; set; }
    bool ResponsePopupVisible { get; set; }
    bool RequestSucceeded { get; set; }
    bool ConfirmationTouched { get; set; }
    bool IsEmailValid { get; }
    bool CanRequestReset { get; }
    bool IsConfirmationValid { get; }
    bool CanConfirmReset { get; }
    bool CanContinueCode { get; }
    void Initialize();
    bool AdvanceToPassword();
    Task RequestResetAsync(CancellationToken token);
    Task<bool> ConfirmResetAsync(CancellationToken token);
}
