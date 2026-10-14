using System.Text.RegularExpressions;
using System.Xml.Linq;

internal sealed record BindingsProject(
    string Directory,
    string Project,
    string Name,
    string Startup,
    string ApplicationId
);

internal static partial class Preview
{
    private static BindingsProject ReadBindingsProject(string path)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
            path = Directory.GetFiles(path, "*.csproj").Single();
        var doc = XDocument.Load(path);
        string Property(string name, string fallback) =>
            doc.Descendants(name).FirstOrDefault()?.Value ?? fallback;
        var name = Property("AssemblyName", Path.GetFileNameWithoutExtension(path));
        var startup = Property(
            "DotNativeStartupType",
            Property("RootNamespace", name) + ".Program"
        );
        var id = Property("ApplicationId", "com.dotnative." + name.ToLowerInvariant());
        if (
            !Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_.]*$")
            || !Regex.IsMatch(startup, "^[A-Za-z_][A-Za-z0-9_.]*$")
            || !Regex.IsMatch(id, "^[A-Za-z][A-Za-z0-9_.]+$")
        )
            throw new ArgumentException(
                "Invalid bindings assembly name, startup type or application ID."
            );
        return new(Path.GetDirectoryName(path)!, path, name, startup, id);
    }

    private static string WriteBindingsProject(
        BindingsProject app,
        string platform,
        string tfm,
        string? applePlugin
    )
    {
        var output = Path.Combine(
            Root,
            "artifacts/bindings-integration/projects",
            platform,
            app.Name
        );
        Directory.CreateDirectory(output);
        var source = XDocument.Load(app.Project);
        var references = new XElement("ItemGroup");
        foreach (var reference in source.Descendants("ProjectReference"))
        {
            var include = (string?)reference.Attribute("Include") ?? "";
            if (include.Contains('$'))
                throw new NotSupportedException(
                    "Bindings project references currently require literal paths."
                );
            var copy = new XElement(reference);
            copy.SetAttributeValue("Include", Path.GetFullPath(include, app.Directory));
            var removed =
                (string?)copy.Attribute("GlobalPropertiesToRemove")
                ?? copy.Element("GlobalPropertiesToRemove")?.Value;
            copy.Element("GlobalPropertiesToRemove")?.Remove();
            copy.SetAttributeValue(
                "GlobalPropertiesToRemove",
                removed + ";RuntimeIdentifier;PublishAot;PublishAotUsingRuntimePack;NativeLib"
            );
            references.Add(copy);
        }
        foreach (var reference in source.Descendants("PackageReference"))
        {
            if ((string?)reference.Attribute("Include") == "DotNative.App")
                continue;
            references.Add(new XElement(reference));
        }
        var project = new XElement(
            "Project",
            new XElement(
                "Import",
                new XAttribute("Project", "Sdk.props"),
                new XAttribute("Sdk", "Microsoft.NET.Sdk")
            ),
            new XElement(
                "PropertyGroup",
                new XElement("TargetFramework", tfm),
                new XElement("AssemblyName", app.Name),
                new XElement("OutputType", "Exe"),
                new XElement("RootNamespace", app.Name),
                new XElement("EnableDefaultCompileItems", "false"),
                new XElement("PublishAot", "true"),
                new XElement(
                    "PublishAotUsingRuntimePack",
                    platform == "android" ? "true" : "false"
                ),
                new XElement("DotNativeAppDirectory", app.Directory),
                new XElement("DotNativeStartupType", app.Startup),
                new XElement("ApplicationId", app.ApplicationId),
                new XElement("ApplicationTitle", app.Name)
            ),
            new XElement(
                "ItemGroup",
                new XElement(
                    "Compile",
                    new XAttribute("Include", Path.Combine(app.Directory, "**/*.cs")),
                    new XAttribute(
                        "Exclude",
                        string.Join(
                            ";",
                            new[] { "bin", "obj", "artifacts", "Platform" }.Select(folder =>
                                Path.Combine(app.Directory, folder, "**")
                            )
                        )
                    )
                )
            ),
            references,
            new XElement(
                "Import",
                new XAttribute("Project", "Sdk.targets"),
                new XAttribute("Sdk", "Microsoft.NET.Sdk")
            ),
            new XElement(
                "Import",
                new XAttribute("Project", Path.Combine(Root, "eng/DotNative.Bindings.targets"))
            )
        );
        if (applePlugin is not null)
            project.Add(
                new XElement(
                    "Import",
                    new XAttribute(
                        "Project",
                        Path.Combine(
                            Path.GetDirectoryName(applePlugin)!,
                            "apple-dependencies.targets"
                        )
                    )
                ),
                new XElement(
                    "ItemGroup",
                    new XElement("NativeLibrary", new XAttribute("Include", applePlugin)),
                    new XElement("LinkerArgument", new XAttribute("Include", "-L/usr/lib/swift"))
                )
            );
        var path = Path.Combine(output, app.Name + ".csproj");
        new XDocument(project).Save(path);
        return path;
    }
}
