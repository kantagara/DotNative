using DotNative;
using DotNative.FilePicker;
using Microsoft.Extensions.DependencyInjection;
using WidgetSamples.Screens;
using WidgetSamples.Services;

namespace WidgetSamples;

public static class Program
{
    public static async Task Main()
    {
        var builder = DotNativeApplication.CreateBuilder();
        builder.Services.AddFilePicker();
        builder.Services.AddSingleton<ISampleInfo, SampleInfo>();
        builder.Services.AddSingleton<DemoStore>();
        builder.Services.AddSingleton<NavigationActions>();
        builder.Services.AddScoped<CartVisit>();
        builder.Services.AddDotNativeNavigation();
        var app = builder.Build();
        await app.Services.GetRequiredService<ISampleInfo>().InitializeAsync(app.Stopping);
        await app.Services.GetRequiredService<Navigator>().PushAsync<HomeScreen>("Widget samples");
        app.Run<App>();
    }
}
