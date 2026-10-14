using DotNative;
using Microsoft.Extensions.DependencyInjection;
using NativeProbe;

namespace InteractionApp;

public static class Program
{
    public static async Task Main()
    {
        var builder = DotNativeApplication.CreateBuilder().UseNativeProbe();
        var app = builder.Build();
        app.Run<App>();
        await Task.Delay(500);
        try
        {
            var service = app.Services.GetRequiredService<NativeService>();
            var first = await service.ReadAsync();
            var second = await service.ReadAsync();
            if (string.IsNullOrEmpty(first) || first != second)
                throw new Exception("Unstable native reply");
            App.Result.Value = "PASS " + first;
        }
        catch (Exception ex)
        {
            App.Result.Value = "FAIL " + ex.Message;
        }
        Console.WriteLine("PLUGIN_RESULT " + App.Result.Value);
    }
}

public sealed class App : Component
{
    internal static readonly State<string> Result = new("Calling native plugin…");

    public override Element Build() => new VStack(new Text(Result.Value)).Padding(24);
}
