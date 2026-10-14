using System.Xml.Linq;

internal static class PluginExample
{
    internal static void Create(
        string pluginDirectory,
        string name,
        string symbol,
        bool managed,
        string template
    )
    {
        var exampleName = symbol + "Demo";
        var output = Path.Combine(pluginDirectory, "example");
        Directory.CreateDirectory(output);
        foreach (var file in Directory.GetFiles(template, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(template, file);
            if (
                relative
                    .Split(Path.DirectorySeparatorChar)
                    .Any(part => part is "bin" or "obj" or ".template.config")
            )
                continue;
            var destination = Path.Combine(output, relative.Replace("AppName", exampleName));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Path.GetExtension(file) is ".jar" or ".png" or ".jpg" or ".jpeg")
                File.Copy(file, destination);
            else
                File.WriteAllText(
                    destination,
                    File.ReadAllText(file).Replace("AppName", exampleName)
                );
        }
        var project = Path.Combine(output, exampleName + ".csproj");
        var document = XDocument.Load(project);
        if (managed)
            document.Descendants("TargetFramework").Single().Value = "net10.0";
        document
            .Root!.Element("PropertyGroup")!
            .Add(new XElement("ImplicitUsings", "enable"), new XElement("Nullable", "enable"));
        document.Root!.Add(
            new XElement(
                "ItemGroup",
                new XElement(
                    "ProjectReference",
                    new XAttribute("Include", "../" + name + ".csproj")
                )
            )
        );
        document.Save(project);
        File.WriteAllText(
            Path.Combine(output, "Program.cs"),
            $$"""
using DotNative;
using {{name}};

namespace {{exampleName}};

public static class Program
{
    public static void Main()
    {
        var builder = DotNativeApplication.CreateBuilder();
        builder.Services.Add{{symbol}}();
        builder.Build().Run<App>();
    }
}
"""
        );
        File.WriteAllText(
            Path.Combine(output, "App.cs"),
            $$"""
using DotNative;
using {{name}};

namespace {{exampleName}};

public sealed class App(NativeService plugin) : Component
{
    private readonly State<string> result = new("Ready");
    private readonly State<bool> busy = new(false);

    public override Element Build() =>
        new VStack(
            new Text("{{name}}").FontSize(24),
            new Text(result.Value).FontSize(18),
            new Button(busy.Value ? "Reading…" : "Read native value", () => _ = Read())
        ).Padding(24).BackgroundColor(Color.White);

    private async Task Read()
    {
        if (busy.Value) return;
        busy.Value = true;
        try { result.Value = {{(
                managed ? "await Task.FromResult(plugin.Read())" : "await plugin.ReadAsync()"
            )}}; }
        catch (Exception error) { result.Value = error.Message; }
        finally { busy.Value = false; }
    }
}
"""
        );
        File.WriteAllText(
            Path.Combine(pluginDirectory, "README.md"),
            $$"""
# {{name}}

The example registers this plugin through `Add{{symbol}}()` and resolves its service through constructor injection.
Edit the C# API or the implementation under `Platform`, then save while the example is running.

```sh
dotnative devices
dotnative plugin run ios --project .
```

{{(
                managed
                    ? "This managed plugin uses direct platform bindings. The runner selects bindings mode; remote mobile hot reload cannot execute device SDK bindings."
                    : "C# edits use hot reload. Native or project changes rebuild and relaunch the example."
            )}}

The plugin uses the `DotNative` NuGet package by default. For SDK development,
copy `dotnative.local.props.example` to `dotnative.local.props` and set `DotNativeSourceRoot`.
The local override is ignored by Git. Each developer chooses their own SDK checkout.
"""
        );
    }
}
