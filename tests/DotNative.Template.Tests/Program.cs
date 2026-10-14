using System.Diagnostics;
using System.Xml.Linq;

var repo = new DirectoryInfo(Environment.CurrentDirectory);
while (!File.Exists(Path.Combine(repo.FullName, "templates/DotNative.App/AppName.csproj")))
    repo =
        repo.Parent ?? throw new InvalidOperationException("Run inside the DotNative repository.");

// Samples may declare plugin dependencies without changing template scaffolding.
// Each permitted addition is explicit: a changed or missing reference still fails.
var pickerReference = new XElement(
    "ProjectReference",
    new XAttribute("Include", "../../plugins/DotNative.FilePicker/DotNative.FilePicker.csproj"),
    new XAttribute(
        "GlobalPropertiesToRemove",
        "RuntimeIdentifier;PublishAot;PublishAotUsingRuntimePack;NativeLib"
    )
);
var pickerImport = new XElement(
    "Import",
    new XAttribute(
        "Project",
        "../../plugins/DotNative.FilePicker/buildTransitive/DotNative.FilePicker.targets"
    )
);
var bindingsImport = new XElement(
    "Import",
    new XAttribute("Project", "../../eng/DotNative.Bindings.Sample.props"),
    new XAttribute("Condition", "'$(DotNativeBindingsPlatform)' != ''")
);

foreach (
    var (name, bundle, additions) in new (string, string, XElement[])[]
    {
        ("WidgetSamples", "com.dotnative.widgetsamples", [pickerReference, pickerImport]),
        ("Counter", "com.dotnative.counter", [bindingsImport]),
    }
)
{
    var temporary = Directory.CreateTempSubdirectory("dotnative-template-check-");
    try
    {
        var generated = Path.Combine(temporary.FullName, name);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = repo.FullName };
        foreach (
            var argument in new[]
            {
                "new",
                "dotnative",
                "-n",
                name,
                "-o",
                generated,
                "--bundleId",
                bundle,
            }
        )
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new Exception("dotnet new failed.");

        var sample = Path.Combine(repo.FullName, "Samples", name);
        foreach (var file in Directory.EnumerateFiles(generated, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(generated, file);
            var actual = Path.Combine(sample, relative);
            if (relative == name + ".csproj")
            {
                var expected = XElement.Load(file);
                var project = XElement.Load(actual);
                VerifyProject(project, expected, additions);
                // Guard the verifier itself: accepting plugin references must
                // not accidentally accept a changed target framework.
                var changed = new XElement(project);
                changed.Descendants("TargetFramework").Single().Value = "net0.0";
                MustReject(() => VerifyProject(changed, expected, additions));
                foreach (var addition in additions)
                {
                    var missing = new XElement(project);
                    missing
                        .Descendants(addition.Name)
                        .Single(n => XNode.DeepEquals(n, addition))
                        .Remove();
                    MustReject(() => VerifyProject(missing, expected, additions));
                }
            }
            else if (
                relative == ".gitignore"
                || relative.StartsWith("Platform" + Path.DirectorySeparatorChar)
            )
            {
                if (!File.ReadAllBytes(actual).AsSpan().SequenceEqual(File.ReadAllBytes(file)))
                    throw new InvalidDataException($"{name}/{relative} differs from template.");
            }
        }
        Console.WriteLine(
            $"PASS {name}: project scaffolding, declared plugins and native runners match."
        );
    }
    finally
    {
        temporary.Delete(recursive: true);
    }
}

static void VerifyProject(XElement actual, XElement expected, XElement[] additions)
{
    var normalized = new XElement(actual);
    foreach (var addition in additions)
    {
        var matches = normalized
            .Descendants(addition.Name)
            .Where(n => XNode.DeepEquals(n, addition))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Missing or changed sample dependency: {addition}");
        matches[0].Remove();
    }
    if (!XNode.DeepEquals(normalized, expected))
        throw new InvalidDataException("Sample project scaffolding differs from template.");
}

static void MustReject(Action check)
{
    try
    {
        check();
    }
    catch (InvalidDataException)
    {
        return;
    }
    throw new Exception("Scaffolding verifier accepted an invalid project.");
}
