using System.Net;
using System.Text.Json;
using FaliNamIgrac;

var d = new Dispatcher();
var games = new FakeGames();
var session = new SessionNotifier(d, new FakeSession(), new FakeRegistration(), games);
games.CurrentProfile = FakeGames.Profile;
d.Finish(session.SignInAsync("fixture@example.com", "fixture", default));
var presentation = new PresentationState();
var api = new AccountFake();
var account = new AccountNotifier(d, session, api, presentation);
void Finish(Task task) => d.Finish(task);
void Check(bool condition, string name)
{
    if (!condition)
        throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
Finish(account.LoadAsync(default));
account.Edit(x => x with { Firstname = " Changed " });
account.ToggleNotifications("tennis");
Finish(account.SaveAsync(default));
Check(
    api.Saved?.Firstname == "Changed"
        && api.Saved.Gender == "F"
        && api.Saved.Phone == "+381641234567"
        && api.Saved.Username == "test@example.com"
        && api.Saved.Photo == "old.jpg"
        && api.Saved.Sports[0].SkillLevel == 4
        && api.Saved.Sports[0].NotificationType == "OFF",
    "save preserves untouched fields, skills, photo and notifications"
);
var saved = account.State.Saved;
api.FailSave = true;
account.Edit(x => x with { Lastname = "Unsaved" });
Finish(account.SaveAsync(default));
Check(
    account.State.Saved == saved
        && account.State.Draft?.Lastname == "Unsaved"
        && account.State.Message.Length > 0,
    "failed save retains saved profile and retryable draft"
);
api.FailSave = false;
account.CancelEdit();
account.SetBirthDate(new DateOnly(2100, 1, 1));
var calls = api.Saves;
Finish(account.SaveAsync(default));
Check(api.Saves == calls, "future birth date cannot reach backend");
account.CancelEdit();
account.Edit(x => x with { Sports = [] });
Finish(account.SaveAsync(default));
Check(api.Saves == calls, "empty sports rejected");
account.CancelEdit();
var places = new PlacesFake();
account.SetLocationQuery("Beograd");
Finish(account.SaveAsync(default));
Check(api.Saves == calls, "unresolved location blocks save");
Finish(account.SearchPlacesAsync(places, default));
Check(account.State.Suggestions?.Count == 1, "location search results");
Finish(account.SelectPlaceAsync(places, account.State.Suggestions![0], default));
Finish(account.SaveAsync(default));
Check(
    api.Saved?.Latitude == 44.81
        && api.Saved.Longitude == 20.46
        && api.Saved.Address == "Beograd, Srbija",
    "selected location persists coordinates and address"
);
places.Fail = true;
account.SetLocationQuery("Invalid");
Finish(account.SearchPlacesAsync(places, default));
Check(
    account.State.Message.Length > 0 && account.State.Draft?.Latitude == 44.81,
    "failed Places request preserves previous location"
);
account.CancelEdit();
var png = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="
);
var remembered = 0;
Finish(
    account.UploadPhotoAsync(
        _ => Task.FromResult<byte[]?>(png),
        (_, _, _) => Task.FromException(new IOException()),
        (_, _) =>
        {
            remembered++;
            return Task.CompletedTask;
        },
        default
    )
);
Check(
    account.State.Saved?.Photo == "old.jpg" && remembered == 0,
    "failed photo upload retains old photo and cache"
);
var picker = new TaskCompletionSource<byte[]?>();
var uploading = account.UploadPhotoAsync(
    _ => picker.Task,
    (_, _, _) =>
    {
        remembered++;
        return Task.CompletedTask;
    },
    (_, _) => Task.CompletedTask,
    default
);
Finish(session.SignOutAsync(default));
games.CurrentProfile = FakeGames.Profile;
d.Finish(session.SignInAsync("fixture@example.com", "fixture", default));
picker.SetResult(png);
Finish(uploading);
Check(
    remembered == 0 && account.State.Saved is null,
    "logout and re-login during picker prevents upload"
);
Finish(account.LoadAsync(default));
var response = new TaskCompletionSource<AccountData>();
api.Load = _ => response.Task;
var loading = account.LoadAsync(default);
Finish(account.LoadAsync(default));
games.CurrentProfile = FakeGames.Profile with { Id = "other" };
d.Finish(session.SignInAsync("other@example.com", "fixture", default));
response.SetResult(AccountFake.Data);
Finish(loading);
Check(account.State.Saved is null, "stale response after account switch discarded");

var remote = new DeletionFake();
var journal = new JournalFake();
var integration = new IntegrationFake();
var workflow = new AccountDeletionWorkflow(remote, journal, integration);
remote.FailIdentity = true;
try
{
    Finish(workflow.RunAsync("42", _ => { }, default));
    throw new Exception("Expected failure");
}
catch (IOException) { }
Check(
    journal.Stage == AccountDeletionStage.BackendDeleted
        && remote.Backends == 1
        && integration.Cleared == 0,
    "partial deletion never cleans session or reports success"
);
remote.FailIdentity = false;
Finish(workflow.RunAsync("42", _ => { }, default));
Check(
    remote.Backends == 1
        && remote.Identities == 2
        && integration.Cleared == 1
        && integration.Finished == 1,
    "retry resumes Cognito deletion without repeating backend delete"
);
var cancel = new CancellationTokenSource();
integration = new IntegrationFake { SessionToken = cancel.Token };
remote = new DeletionFake { AfterBackend = () => cancel.Cancel() };
journal = new JournalFake();
workflow = new(remote, journal, integration);
try
{
    Finish(workflow.RunAsync("42", _ => { }, default));
    throw new Exception("Expected cancellation");
}
catch (OperationCanceledException) { }
Check(
    remote.Identities == 0 && integration.Cleared == 0,
    "session change between deletion stages stops further destructive calls"
);

var handler = new PhotoHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid/") };
var storage = new StorageFake();
var upload = new AccountPhotoUpload(http, new FakeSession(), storage);
handler.Status = HttpStatusCode.InternalServerError;
try
{
    Finish(upload.UploadAsync("42", png, default));
    throw new Exception("Expected HTTP failure");
}
catch (HttpRequestException) { }
using var body = JsonDocument.Parse(handler.Body!);
Check(
    storage.Key != "42"
        && handler.Path == "/user-data/update-profile-picture"
        && body.RootElement.GetProperty("userId").GetInt32() == 42
        && "42" + body.RootElement.GetProperty("extension").GetString() == storage.Key + ".png",
    "failed photo pointer save preserves old storage key; generated request matches backend contract"
);
var before = handler.Calls;
storage.Fail = true;
try
{
    Finish(upload.UploadAsync("42", png, default));
    throw new Exception("Expected upload failure");
}
catch (IOException) { }
Check(handler.Calls == before, "failed storage upload never changes backend photo pointer");
games.CurrentProfile = FakeGames.Profile;
d.Finish(session.SignInAsync("fixture@example.com", "fixture", default));
remote = new DeletionFake();
journal = new JournalFake();
integration = new IntegrationFake();
workflow = new(remote, journal, integration);
using (var deletion = new AccountDeletionNotifier(d, session, workflow))
{
    Finish(deletion.ConfirmAsync(default));
    Check(remote.Backends == 0, "delete requires explicit confirmation");
    deletion.AskConfirmation();
    Finish(session.SignOutAsync(default));
    games.CurrentProfile = FakeGames.Profile;
    d.Finish(session.SignInAsync("fixture@example.com", "fixture", default));
    Finish(deletion.ConfirmAsync(default));
    Check(remote.Backends == 0, "old confirmation cannot delete reauthenticated account");
    deletion.AskConfirmation();
    remote.FailIdentity = true;
    Finish(deletion.ConfirmAsync(default));
    Check(
        deletion.State.Failed
            && !deletion.State.Completed
            && deletion.State.Stage == AccountDeletionStage.BackendDeleted,
        "partial failure remains visible in deletion notifier"
    );
    remote.FailIdentity = false;
    Finish(deletion.ConfirmAsync(default));
    Check(
        deletion.State.Completed && remote.Backends == 1,
        "confirmed retry completes all required stages"
    );
}
handler.Status = HttpStatusCode.OK;
using (var client = new ApiClient(http, new FakeSession()))
{
    var service = new AccountService(client, new FakeSession());
    Finish(service.SaveAsync(AccountFake.Data with { Id = "42", Bio = null }, default));
    using var request = JsonDocument.Parse(handler.Body!);
    Check(
        handler.Path == "/user-data/update-user"
            && request.RootElement.GetProperty("bio").ValueKind == JsonValueKind.Null
            && request.RootElement.GetProperty("gender").GetString() == "F"
            && request
                .RootElement.GetProperty("sports")[0]
                .GetProperty("notificationType")
                .GetString() == "ON",
        "real account HTTP serialization preserves nullable fields and sport contract"
    );
}
Check(!AccountValidation.TryBirthDate("2000-01-01Tgarbage", out _), "malformed timestamp rejected");
var cleanupCalls = 0;
using (
    var guarded = new SessionAccountDeletionIntegration(
        session,
        new FakeSession(),
        (_, _) => Task.CompletedTask,
        (_, _) =>
        {
            cleanupCalls++;
            return Task.CompletedTask;
        },
        (_, _) => Task.CompletedTask
    )
)
{
    AccountDeletionLease? lease = null;
    Finish(Capture());
    async Task Capture()
    {
        lease = await guarded.CaptureAsync(
            FakeGames.Profile.Id,
            AccountDeletionStage.IdentityDeleted,
            default
        );
    }
    Check(lease!.AccessToken == "", "cleanup recovery does not refresh a deleted Cognito identity");
    Finish(session.SignOutAsync(default));
    games.CurrentProfile = FakeGames.Profile;
    d.Finish(session.SignInAsync("fixture@example.com", "fixture", default));
    try
    {
        Finish(guarded.ClearAccountDataAsync(lease, default));
        throw new Exception("Expected stale lease");
    }
    catch (OperationCanceledException) { }
    Check(cleanupCalls == 0, "old deletion lease cannot clear reauthenticated session data");
}
api.Load = _ => Task.FromResult(AccountFake.Data);
Finish(account.LoadAsync(default));
account.ToggleSport("tennis");
account.ToggleSport("tennis");
Check(
    account.State.Draft!.Sports[0].SkillLevel == 4,
    "reselecting a sport preserves its saved skill"
);
var saveGate = new TaskCompletionSource();
api.Save = () => saveGate.Task;
var saving = account.SaveAsync(default);
var saveCalls = api.Saves;
Finish(account.SaveAsync(default));
Check(api.Saves == saveCalls, "duplicate save cannot send twice");
saveGate.SetResult();
Finish(saving);
var photoReload = new TaskCompletionSource<AccountData>();
api.Load = _ => photoReload.Task;
var caching = 0;
var photoRequest = account.UploadPhotoAsync(
    _ => Task.FromResult<byte[]?>(png),
    (_, _, _) => Task.CompletedTask,
    (_, _) =>
    {
        caching++;
        return Task.CompletedTask;
    },
    default
);
games.CurrentProfile = FakeGames.Profile with { Id = "other" };
d.Finish(session.SignInAsync("other@example.com", "fixture", default));
photoReload.SetResult(AccountFake.Data with { Photo = "new.png" });
Finish(photoRequest);
Check(
    caching == 0 && account.State.Saved is null,
    "stale photo refresh cannot cache bytes or update a different account"
);
using (
    var cognito = new Amazon.CognitoIdentityProvider.AmazonCognitoIdentityProviderClient(
        new Amazon.Runtime.AnonymousAWSCredentials(),
        Amazon.RegionEndpoint.USEast1
    )
)
{
    var deletionHttp = new AccountDeletionRemote(http, cognito);
    handler.Status = HttpStatusCode.NotFound;
    try
    {
        Finish(deletionHttp.DeleteBackendAsync("42", "snapshot", default));
        throw new Exception("Expected unconfirmed deletion");
    }
    catch (HttpRequestException) { }
    Check(
        handler.Path == "/users/42"
            && handler.Method == HttpMethod.Delete
            && handler.Bearer == "snapshot",
        "backend deletion uses verified DELETE route and credential snapshot; 404 is not success"
    );
}
remote = new DeletionFake();
journal = new JournalFake();
integration = new IntegrationFake { FailClear = true };
workflow = new(remote, journal, integration);
try
{
    Finish(workflow.RunAsync("42", _ => { }, default));
    throw new Exception("Expected cleanup failure");
}
catch (IOException) { }
Check(
    journal.Stage == AccountDeletionStage.IdentityDeleted && integration.Finished == 0,
    "local cleanup failure cannot announce account deletion complete"
);
integration.FailClear = false;
Finish(workflow.RunAsync("42", _ => { }, default));
Check(
    remote.Backends == 1 && remote.Identities == 1 && integration.Finished == 1,
    "cleanup retry does not repeat either remote deletion"
);
Console.WriteLine("All feature 04 tests passed.");

sealed class AccountFake : IAccountService
{
    public static readonly AccountData Data = new()
    {
        Id = FakeGames.Profile.Id,
        Username = "test@example.com",
        Firstname = "A",
        Lastname = "B",
        Nickname = "ab",
        BirthDate = "2000-01-01T00:00:00",
        Gender = "F",
        Phone = "+381641234567",
        Photo = "old.jpg",
        Sports = [new("tennis", 4, "ON")],
    };
    public Func<CancellationToken, Task<AccountData>> Load = _ => Task.FromResult(Data);
    public AccountData? Saved;
    public bool FailSave;
    public int Saves;
    public Func<Task> Save = () => Task.CompletedTask;

    public Task<AccountData> LoadAsync(CancellationToken token) => Load(token);

    public Task SaveAsync(AccountData data, CancellationToken token)
    {
        Saves++;
        if (FailSave)
            throw new IOException();
        Saved = data;
        return Save();
    }
}

sealed class PlacesFake : IPlacesService
{
    public bool Fail;

    public Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(string text, CancellationToken token) =>
        Fail
            ? Task.FromException<IReadOnlyList<PlaceSuggestion>>(new IOException())
            : Task.FromResult<IReadOnlyList<PlaceSuggestion>>([new("Beograd, Srbija", "city")]);

    public Task<SelectedPlace> GetDetailsAsync(PlaceSuggestion place, CancellationToken token) =>
        Task.FromResult(new SelectedPlace(place.Description, 44.81, 20.46));
}

sealed class DeletionFake : IAccountDeletionRemote
{
    public int Backends,
        Identities;
    public bool FailIdentity;
    public Action? AfterBackend;

    public Task DeleteBackendAsync(string id, string accessToken, CancellationToken token)
    {
        Backends++;
        AfterBackend?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteIdentityAsync(string accessToken, CancellationToken token)
    {
        Identities++;
        return FailIdentity ? Task.FromException(new IOException()) : Task.CompletedTask;
    }
}

sealed class JournalFake : IAccountDeletionJournal
{
    public AccountDeletionStage Stage;

    public Task<AccountDeletionStage> ReadAsync(string id, CancellationToken token) =>
        Task.FromResult(Stage);

    public Task WriteAsync(string id, AccountDeletionStage stage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Stage = stage;
        return Task.CompletedTask;
    }
}

sealed class IntegrationFake : IAccountDeletionIntegration
{
    public CancellationToken SessionToken;
    public int Cleared,
        Finished;
    public bool FailClear;

    public Task<AccountDeletionLease> CaptureAsync(
        string id,
        AccountDeletionStage stage,
        CancellationToken token
    ) => Task.FromResult(new AccountDeletionLease(id, "test-token", SessionToken));

    public Task DetachPushAsync(AccountDeletionLease lease, CancellationToken token) =>
        Task.CompletedTask;

    public Task ClearAccountDataAsync(AccountDeletionLease lease, CancellationToken token)
    {
        Cleared++;
        return FailClear ? Task.FromException(new IOException()) : Task.CompletedTask;
    }

    public Task FinishAsync(AccountDeletionLease lease, CancellationToken token)
    {
        Finished++;
        return Task.CompletedTask;
    }
}

sealed class StorageFake : IProfilePhotoStorage
{
    public string? Key;
    public bool Fail;

    public Task UploadAsync(string userId, byte[] photo, CancellationToken token)
    {
        Key = userId;
        return Fail ? Task.FromException(new IOException()) : Task.CompletedTask;
    }
}

sealed class PhotoHandler : HttpMessageHandler
{
    public HttpStatusCode Status;
    public string? Body,
        Path,
        Bearer;
    public HttpMethod? Method;
    public int Calls;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken token
    )
    {
        Calls++;
        Path = request.RequestUri!.AbsolutePath;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
        Method = request.Method;
        Bearer = request.Headers.Authorization?.Parameter;
        return new(Status);
    }
}
