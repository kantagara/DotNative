using DotNative;
using Microsoft.Extensions.DependencyInjection;

namespace Counter;

public static class Program
{
    public static void Main()
    {
        var builder = DotNativeApplication.CreateBuilder();
        // Register application services here: builder.Services.AddSingleton<IService, Service>();
        builder.Build().Run<App>();
    }
}
