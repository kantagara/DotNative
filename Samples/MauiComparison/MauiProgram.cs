using Microsoft.Extensions.DependencyInjection;

namespace MauiComparison;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.Services.AddSingleton<SampleStore>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddScoped<CartVisit>();
        builder.Services.AddScoped<CartPage>();
        return builder.Build();
    }
}
