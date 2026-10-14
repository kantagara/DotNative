using System.Net;
using DotNative;
using DotNative.Hive;
using FaliNamIgrac;

internal static class MediaCacheTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "fni-cache-tests-" + Guid.NewGuid());
        try
        {
            Chat(root);
            PhotoFlow();
            ReactiveImage(root);
            Images(root).GetAwaiter().GetResult();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void Chat(string root)
    {
        using var f = new Fixture();
        f.SignIn();
        var cache = new ChatCache(
            new HiveStore(Path.Combine(root, "chat"), recoverCorruptBoxes: true)
        );
        var received = new ChatMessage(
            "old",
            "room",
            "other",
            "Saved history",
            DateTimeOffset.UtcNow,
            []
        );
        var api = new FakeChat
        {
            Messages = () => Task.FromResult<IReadOnlyList<ChatMessage>>([received]),
        };
        var first = new ChatNotifier(f.Dispatcher, f.Session, api, f.Presentation, cache);
        f.Dispatcher.Finish(first.RefreshAsync(default));
        f.Dispatcher.Finish(first.OpenAsync(FakeChat.Room, default));
        first.SetDraft("Restart-safe message");
        ChatMessage? accepted = null;
        api.Send = message =>
        {
            accepted = message;
            return Task.FromException<ChatMessage>(
                new HttpRequestException("Lost acknowledgement")
            );
        };
        f.Dispatcher.Finish(first.SendAsync(default));
        f.Dispatcher.Finish(first.FlushAsync());
        Check(accepted is not null, "Send never reached server after durable commit");
        first.Clear();
        api.Messages = () =>
            Task.FromException<IReadOnlyList<ChatMessage>>(new HttpRequestException("Offline"));
        var second = new ChatNotifier(
            f.Dispatcher,
            f.Session,
            api,
            f.Presentation,
            new ChatCache(new HiveStore(Path.Combine(root, "chat"), recoverCorruptBoxes: true))
        );
        f.Dispatcher.Finish(second.OpenAsync(FakeChat.Room, default));
        Check(
            second.State.Messages?.Any(item => item.Id == "old") == true
                && second.State.Messages.Any(item => item.Id == accepted!.Id)
                && second.State.Rooms?.Count == 1,
            "Offline restart lost cached conversations"
        );
        Check(
            second.State.Draft == "" && second.HasPendingMessage,
            "Offline restart lost optimistic retry state"
        );
        var duplicateSends = 0;
        api.Send = message =>
        {
            duplicateSends++;
            return Task.FromResult(message);
        };
        api.Messages = () => Task.FromResult<IReadOnlyList<ChatMessage>>([received, accepted!]);
        f.Dispatcher.Finish(second.SendAsync(default));
        f.Dispatcher.Finish(second.FlushAsync());
        Check(
            duplicateSends == 0 && second.State.Draft == "",
            "Restart retry duplicated an already accepted message"
        );
        Check(
            cache.ReadAsync("different-user", default).GetAwaiter().GetResult() is null,
            "Cache leaked across accounts"
        );
        var saved = cache.ReadAsync("player", default).GetAwaiter().GetResult()!;
        Check(
            saved.Pending.Count == 0 && saved.Messages["room"].Length == 2,
            "Confirmed state did not persist"
        );

        var failed = new ChatNotifier(
            f.Dispatcher,
            f.Session,
            api,
            f.Presentation,
            new FailingCache()
        );
        f.Dispatcher.Finish(failed.OpenAsync(FakeChat.Room, default));
        failed.SetDraft("Must remain a draft");
        f.Dispatcher.Finish(failed.SendAsync(default));
        Check(
            duplicateSends == 0
                && failed.HasPendingMessage
                && failed.State.Messages!.Any(item => item.Content == "Must remain a draft")
                && failed.State.CacheError.Length > 0,
            "A failed durable commit still sent a message"
        );

        var delayed = new DelayedCache();
        var stale = new ChatNotifier(f.Dispatcher, f.Session, api, f.Presentation, delayed);
        var restore = stale.RefreshAsync(default);
        stale.Clear();
        delayed.Result.SetResult(saved);
        f.Dispatcher.Finish(restore);
        Check(
            stale.State.Rooms is null && stale.State.Draft == "",
            "Old hydration crossed the session boundary"
        );

        File.WriteAllText(
            Path.Combine(root, "chat", "chat-" + HiveKeys.Hash("player")[..48] + ".box"),
            "{broken"
        );
        Check(
            cache.ReadAsync("player", default).GetAwaiter().GetResult() is null,
            "Corrupt box blocked startup"
        );
        f.Dispatcher.Finish(second.FlushAsync());
        f.Dispatcher.Finish(failed.FlushAsync());
    }

    private static void ReactiveImage(string root)
    {
        using var f = new Fixture();
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lWQAAAAASUVORK5CYII="
        );
        var handler = new ImageHandler(png);
        using var http = new HttpClient(handler);
        using var repository = new ImageRepository(Path.Combine(root, "reactive"), http);
        var images = new RemoteImages(repository, f.Dispatcher, default);
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Probe(() =>
        {
            if (images.Get("https://example.test/reactive.png") is not { } source)
                return new Text("Initials");
            rendered.TrySetResult();
            return new Image(source).Width(40).Height(40);
        });
        using var host = new RenderHost(probe, f.Dispatcher, new Sink());
        host.RenderNow();
        f.Dispatcher.Finish(handler.Started.Task);
        Check(probe.Builds == 1, "Initial image render did not retain its placeholder");
        handler.Release.TrySetResult();
        f.Dispatcher.Finish(rendered.Task);
        Check(probe.Builds >= 2, "Downloaded photo did not trigger a UI rebuild");
    }

    private static void PhotoFlow()
    {
        using var f = new Fixture();
        f.SignIn();
        var api = new FakeAccount();
        var account = new AccountNotifier(f.Dispatcher, f.Session, api, f.Presentation);
        f.Dispatcher.Finish(account.LoadAsync(default));
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lWQAAAAASUVORK5CYII="
        );
        var uploads = 0;
        string? remembered = null;
        Task Upload(string user, byte[] bytes, CancellationToken token)
        {
            Check(
                user == "player" && PhotoFormat.Detect(bytes).Extension == ".png",
                "Incorrect photo owner or format"
            );
            uploads++;
            api.Load = () => Task.FromResult(FakeAccount.Data with { Photo = "player.png" });
            return Task.CompletedTask;
        }
        Task Remember(string? url, byte[] bytes)
        {
            remembered = url;
            return Task.CompletedTask;
        }
        f.Dispatcher.Finish(
            account.UploadPhotoAsync(_ => Task.FromResult<byte[]?>(null), Upload, Remember, default)
        );
        Check(uploads == 0, "Cancelled picker uploaded a photo");
        var selected = new TaskCompletionSource<byte[]?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var picking = account.UploadPhotoAsync(_ => selected.Task, Upload, Remember, default);
        account.Clear();
        selected.SetResult(png);
        f.Dispatcher.Finish(picking);
        Check(uploads == 0, "Stale picker uploaded to previous account");
        f.Dispatcher.Finish(account.LoadAsync(default));
        f.Dispatcher.Finish(
            account.UploadPhotoAsync(_ => Task.FromResult<byte[]?>(png), Upload, Remember, default)
        );
        Check(
            uploads == 1
                && account.State.Saved?.Photo == "player.png"
                && remembered == "player.png",
            "Successful photo upload did not refresh profile and cache"
        );
    }

    private static async Task Images(string root)
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lWQAAAAASUVORK5CYII="
        );
        var directory = Path.Combine(root, "images");
        var handler = new ImageHandler(png);
        using var http = new HttpClient(handler);
        using (var images = new ImageRepository(directory, http))
        {
            var requests = Enumerable
                .Range(0, 10)
                .Select(_ => images.LoadAsync("https://example.test/a.png"))
                .ToArray();
            await handler.Started.Task;
            handler.Release.SetResult();
            Check(
                (await Task.WhenAll(requests)).All(image => image is not null)
                    && handler.Calls == 1,
                "Concurrent avatar requests were not coalesced"
            );
            Check(
                await images.LoadAsync("file:///etc/passwd") is null,
                "Image loader accepted a local-file URL"
            );
        }
        using (var offline = new ImageRepository(directory, new HttpClient(new OfflineHandler())))
            Check(
                await offline.LoadAsync("https://example.test/a.png") is not null,
                "Restart failed to reuse image disk cache"
            );
        File.SetLastWriteTimeUtc(
            Path.Combine(directory, HiveKeys.Hash("https://example.test/a.png") + ".image"),
            DateTime.UtcNow.AddDays(-2)
        );
        using (var offline = new ImageRepository(directory, new HttpClient(new OfflineHandler())))
            Check(
                await offline.LoadAsync("https://example.test/a.png") is not null,
                "Stale image disappeared offline"
            );
        File.WriteAllText(
            Path.Combine(directory, HiveKeys.Hash("https://example.test/bad.png") + ".image"),
            "not an image"
        );
        using (var offline = new ImageRepository(directory, new HttpClient(new OfflineHandler())))
            Check(
                await offline.LoadAsync("https://example.test/bad.png") is null,
                "Corrupt image did not use placeholder"
            );
        using var oversized = new MemoryStream(new byte[8 * 1024 * 1024 + 1]);
        try
        {
            await ImageRepository.ReadBoundedAsync(oversized, default);
            throw new Exception("Oversized image accepted");
        }
        catch (InvalidDataException) { }
        var badDimensions = png.ToArray();
        badDimensions[16] = 127;
        Check(!ImageRepository.Valid(badDimensions), "Oversized decoded image accepted");
        using (var limited = new ImageRepository(directory, http))
        {
            for (var i = 0; i < 129; i++)
                await limited.StoreAsync("https://example.test/" + i + ".png", png, default);
            Check(
                Directory.GetFiles(directory, "*.image").Length <= 128,
                "Image disk quota exceeded"
            );
        }
        var cancellable = new ImageHandler(png);
        using (var downloads = new ImageRepository(directory, new HttpClient(cancellable)))
        using (var cancel = new CancellationTokenSource())
        {
            var abandoned = downloads.LoadAsync("https://example.test/shared.png", cancel.Token);
            var retained = downloads.LoadAsync("https://example.test/shared.png");
            await cancellable.Started.Task;
            cancel.Cancel();
            try
            {
                await abandoned;
                throw new Exception("Cancelled consumer did not cancel");
            }
            catch (OperationCanceledException) { }
            cancellable.Release.SetResult();
            Check(
                await retained is not null && cancellable.Calls == 1,
                "One cancelled row cancelled another row's image"
            );
        }
        Check(PhotoFormat.Detect(png).ContentType == "image/png", "PNG upload metadata incorrect");
        Check(
            MediaUrl.Profile("42.jpg")
                == "https://fali-nam-igrac-profile-images.s3.amazonaws.com/public/42.jpg",
            "REST filename not resolved"
        );
        Check(
            MediaUrl.Group("public/group-photos/42.png")
                == "https://fali-nam-igrac-profile-images.s3.amazonaws.com/public/group-photos/42.png",
            "Group key not resolved"
        );
        Check(
            MediaUrl.Profile("https://example.test/avatar") == "https://example.test/avatar"
                && MediaUrl.Profile("../private") is null,
            "Media URL normalization incorrect"
        );
        foreach (
            var name in typeof(FniAssets)
                .Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("fni/") && name.EndsWith(".png"))
        )
        {
            using var stream = typeof(FniAssets).Assembly.GetManifestResourceStream(name)!;
            using var output = new MemoryStream();
            stream.CopyTo(output);
            Check(ImageRepository.Valid(output.ToArray()), "Invalid embedded PNG: " + name);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private sealed class FailingCache : IChatCache
    {
        public Task<ChatSnapshot?> ReadAsync(string user, CancellationToken token) =>
            Task.FromResult<ChatSnapshot?>(null);

        public Task WriteAsync(string user, ChatSnapshot snapshot, CancellationToken token) =>
            Task.FromException(new IOException("Disk full"));
    }

    private sealed class DelayedCache : IChatCache
    {
        public TaskCompletionSource<ChatSnapshot?> Result = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task<ChatSnapshot?> ReadAsync(string user, CancellationToken token) => Result.Task;

        public Task WriteAsync(string user, ChatSnapshot snapshot, CancellationToken token) =>
            Task.CompletedTask;
    }

    private sealed class ImageHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Calls;
        public TaskCompletionSource Started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public TaskCompletionSource Release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Interlocked.Increment(ref Calls);
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            return new(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(bytes),
            };
        }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        ) => Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline"));
    }
}
