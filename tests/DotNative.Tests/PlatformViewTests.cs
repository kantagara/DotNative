using DotNative;

internal static class PlatformViewTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        using var host = new RenderHost(
            new Factory(() => new PlatformView("webview").Width(320).Height(180)),
            dispatcher,
            native
        );
        host.RenderNow();
        var node = native.Nodes.Single(item => item.Value.Kind == 16);
        Require(node.Value.ViewType == "webview");
        host.RenderNow();
        Require(native.Nodes[node.Key].ViewType == "webview" && native.Nodes.ContainsKey(node.Key));
        Reject(() => new PlatformView(""));
        Reject(() => new PlatformView("no spaces"));
        Reject(() => new PlatformView(new string('a', 65)));
    }

    private static void Require(bool value)
    {
        if (!value)
            throw new Exception("Platform view assertion failed.");
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new Exception("Invalid platform view type accepted.");
    }
}
