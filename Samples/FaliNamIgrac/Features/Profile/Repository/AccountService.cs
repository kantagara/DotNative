namespace FaliNamIgrac;

public interface IAccountService
{
    Task<AccountData> LoadAsync(CancellationToken token);
    Task SaveAsync(AccountData account, CancellationToken token);
}

public sealed class AccountService(IApiClient api, ISessionService session) : IAccountService
{
    public async Task<AccountData> LoadAsync(CancellationToken token) =>
        (
            await api.GetAsync<ProfileQuery, AccountResponse>(
                "user-data/userByEmail",
                new ProfileQuery(session.Email),
                token,
                authenticated: true
            )
        ).UserData;

    public async Task SaveAsync(AccountData account, CancellationToken token)
    {
        using var response = await api.PutJsonAsync(
            "user-data/update-user",
            new UpdateAccountRequest(
                int.Parse(account.Id, System.Globalization.CultureInfo.InvariantCulture),
                account.Username,
                account.Firstname.Trim(),
                account.Lastname.Trim(),
                account.Nickname.Trim(),
                account.Bio,
                account.BirthDate,
                account.Gender,
                account.Phone,
                account.Latitude,
                account.Longitude,
                account.City,
                account.Address,
                account.Photo,
                account.Sports
            ),
            token,
            authenticated: true
        );
        response.EnsureSuccessStatusCode();
    }
}
