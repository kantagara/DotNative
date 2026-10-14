using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace DotNative.WinUI;

internal sealed partial class IslandApplication : Application, IXamlMetadataProvider
{
    private readonly XamlControlsXamlMetaDataProvider _metadata = new();
    private readonly WindowsXamlManager _manager;

    internal IslandApplication()
    {
        _manager = WindowsXamlManager.InitializeForCurrentThread();
        Resources.MergedDictionaries.Add(new XamlControlsResources());
    }

    public IXamlType GetXamlType(Type type) => _metadata.GetXamlType(type);

    public IXamlType GetXamlType(string name) => _metadata.GetXamlType(name);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();
}

// All exports run on the native host UI thread. Exceptions never cross the ABI.
public static unsafe partial class WinUIBridge
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationContext
    {
        public uint Size,
            Flags;
        public nint Source;
        public ushort Architecture,
            Language;
        public nint Directory,
            Resource,
            Application,
            Module;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateActCtxW", SetLastError = true)]
    private static partial nint CreateActCtx(ref ActivationContext context);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ActivateActCtx(nint context, out nuint cookie);

    private static nint _activationContext;
    private static nuint _activationCookie;

    private static void InitializeActivation()
    {
        string directory = Path.GetDirectoryName(Environment.ProcessPath)!;
        nint source = Marshal.StringToHGlobalUni(
            Path.Combine(directory, "DotNative.WinUI.manifest")
        );
        nint folder = Marshal.StringToHGlobalUni(directory);
        try
        {
            var context = new ActivationContext
            {
                Size = (uint)sizeof(ActivationContext),
                Flags = 4,
                Source = source,
                Directory = folder,
            };
            _activationContext = CreateActCtx(ref context);
            if (
                _activationContext == -1
                || !ActivateActCtx(_activationContext, out _activationCookie)
            )
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(source);
            Marshal.FreeHGlobal(folder);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint mode);

    [LibraryImport("Microsoft.WindowsAppRuntime.dll")]
    private static partial int WindowsAppRuntime_EnsureIsLoaded();

    private static Microsoft.UI.Dispatching.DispatcherQueueController? _queue;
    private static IslandApplication? _application;
    private static nint _next;

    [UnmanagedCallersOnly(EntryPoint = "dotnative_winui_initialize", CallConvs = [typeof(CallConvCdecl)])]
    public static int InitializeRuntime()
    {
        try { EnsureInitialized(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return -1; }
    }

    private static void EnsureInitialized()
    {
        if (_application is null)
        {
            InitializeActivation();
            int com = CoInitializeEx(0, 2);
            if (com < 0)
                Marshal.ThrowExceptionForHR(com);
            Environment.SetEnvironmentVariable(
                "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
                Path.GetDirectoryName(Environment.ProcessPath) + Path.DirectorySeparatorChar
            );
            Environment.SetEnvironmentVariable(
                "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY_PID",
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            Marshal.ThrowExceptionForHR(WindowsAppRuntime_EnsureIsLoaded());
            WinRT.ComWrappersSupport.InitializeComWrappers();
            _queue ??= Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnCurrentThread();
            _application = new IslandApplication();
        }
    }
}
