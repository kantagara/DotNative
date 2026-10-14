namespace FaliNamIgrac;

public sealed record SponsorReservation(Guid Id, SponsorAd Ad);

/// <summary>One repository per authenticated session, shared by all placements. Dispose on logout.
/// Persistent caps survive logout/restart; cached remote config expires each local calendar day.</summary>
public sealed class SponsorRepository : IDisposable
{
    private readonly string account;
    private readonly ISponsorConfigSource source;
    private readonly ISponsorLedgerStore store;
    private readonly TimeProvider clock;
    private readonly Func<int, int> choose;
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<Guid, SponsorAd> reservations = new();
    private SponsorLedger? ledger;
    private bool disposed;

    public SponsorRepository(
        string account,
        ISponsorConfigSource source,
        ISponsorLedgerStore store,
        TimeProvider? clock = null,
        Func<int, int>? choose = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        this.account = account;
        this.source = source;
        this.store = store;
        this.clock = clock ?? TimeProvider.System;
        this.choose = choose ?? Random.Shared.Next;
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private async Task EnsureDayAsync(CancellationToken token)
    {
        ledger ??= await store.ReadAsync(account, token);
        token.ThrowIfCancellationRequested();
        if (ledger is null || ledger.Day != Today)
        {
            ledger = new(Today, null, new(), 0);
            reservations.Clear();
        }
        if (ledger.Counts is null || ledger.Foregrounds < 0 || ledger.Counts.Values.Any(n => n < 0))
            throw new InvalidDataException("Invalid frequency ledger.");
    }

    private async Task<T> Serialized<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken caller
    )
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, caller);
        linked.CancelAfter(TimeSpan.FromSeconds(30));
        await serial.WaitAsync(linked.Token);
        try
        {
            await EnsureDayAsync(linked.Token);
            return await action(linked.Token);
        }
        finally
        {
            serial.Release();
        }
    }

    public Task<SponsorReservation?> ReserveAsync(
        AdPlacement placement,
        CancellationToken token = default
    ) =>
        Serialized<SponsorReservation?>(
            async ct =>
            {
                if (ledger!.Config is null)
                {
                    var config = await source.FetchAsync(ct);
                    ct.ThrowIfCancellationRequested();
                    var updated = ledger with { Config = config };
                    await store.WriteAsync(account, updated, ct);
                    ct.ThrowIfCancellationRequested();
                    ledger = updated;
                }
                var valid = ledger.Config!.For(placement).Where(ad => Eligible(ad)).ToArray();
                if (valid.Length == 0)
                    return null;
                var ad = valid[choose(valid.Length)];
                var id = Guid.NewGuid();
                reservations.Add(id, ad);
                return new(id, ad);
            },
            token
        );

    private bool Eligible(SponsorAd? ad)
    {
        if (
            ad is null
            || string.IsNullOrWhiteSpace(ad.Name)
            || ad.Name.Length > 200
            || ad.DurationDays is <= 0 or > 3650
            || ad.DailyLimit <= 0
        )
            return false;
        try
        {
            PlatformActionLinks.Https(ad.ImageUrl);
            PlatformActionLinks.Https(ad.RedirectUrl);
        }
        catch (ArgumentException)
        {
            return false;
        }
        DateTimeOffset end;
        try
        {
            end = ad.Starts.AddDays(ad.DurationDays);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        var now = clock.GetLocalNow();
        // Flutter permits a campaign throughout its start calendar day.
        return (now >= ad.Starts || DateOnly.FromDateTime(ad.Starts.DateTime) == Today)
            && now < end
            && ledger!.Counts.GetValueOrDefault(ad.Name)
                + reservations.Values.Count(r => r.Name == ad.Name)
                < ad.DailyLimit;
    }

    // Commit only after the image/view actually becomes visible, not from Build().
    public Task<bool> MarkVisibleAsync(Guid id, CancellationToken token = default) =>
        Serialized(
            async ct =>
            {
                if (!reservations.TryGetValue(id, out var ad))
                    return false;
                var counts = new Dictionary<string, int>(ledger!.Counts);
                counts[ad.Name] = checked(counts.GetValueOrDefault(ad.Name) + 1);
                var updated = ledger with { Counts = counts };
                await store.WriteAsync(account, updated, ct);
                ct.ThrowIfCancellationRequested();
                ledger = updated;
                reservations.Remove(id);
                return true;
            },
            token
        );

    public Task ReleaseAsync(Guid id, CancellationToken token = default) =>
        Serialized(ct => Task.FromResult(reservations.Remove(id)), token);

    // Flutter sequence: show on foreground 1, 2, 4, 6... (not on every resume).
    public Task<bool> NextForegroundAsync(CancellationToken token = default) =>
        Serialized(
            async ct =>
            {
                var count = ledger!.Foregrounds;
                var updated = ledger with { Foregrounds = checked(count + 1) };
                await store.WriteAsync(account, updated, ct);
                ct.ThrowIfCancellationRequested();
                ledger = updated;
                return count == 0 || count % 2 == 1;
            },
            token
        );

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        lifetime.Cancel();
        // Do not dispose the semaphore/CTS while a native storage completion may still be in flight.
    }
}
