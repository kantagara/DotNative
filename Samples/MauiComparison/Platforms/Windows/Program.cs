using Microsoft.UI.Dispatching;

namespace MauiComparison.WinUI;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread())
            );
            _ = new WindowsApp();
        });
    }

    internal static void Log(object error) =>
        File.AppendAllText(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DotNative",
                "MauiComparison",
                "startup-error.log"
            ),
            error + Environment.NewLine
        );
}

public sealed class WindowsApp
    : MauiWinUIApplication,
        Microsoft.UI.Xaml.Markup.IXamlMetadataProvider
{
    // C# startup still needs the metadata for MAUI/WinUI's built-in resources.
    // The controls provider also delegates to Core and the WinUI provider.
    private Microsoft.UI.Xaml.Markup.IXamlMetadataProvider? _metadata;
    private Microsoft.UI.Xaml.Markup.IXamlMetadataProvider Metadata =>
        _metadata ??= new Microsoft.Maui.Controls.Controls_Core_XamlTypeInfo.XamlMetaDataProvider();

    public WindowsApp() => UnhandledException += (_, e) => Program.Log(e.Exception);

    public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type type) => Metadata.GetXamlType(type);

    public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(string name) =>
        Metadata.GetXamlType(name);

    public Microsoft.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() =>
        Metadata.GetXmlnsDefinitions();

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
