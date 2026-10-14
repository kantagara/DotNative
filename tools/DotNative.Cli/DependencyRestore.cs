using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static partial class Preview
{
    private static async Task<int> RestoreDependencies(string[] arguments)
    {
        if (arguments.Length != 2 || arguments[0] != "--project")
            throw new ArgumentException(
                "Use: dotnative restore --project <app-directory-or-csproj>"
            );
        var path = Path.GetFullPath(arguments[1]);
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        var project = Directory.Exists(path) ? Directory.GetFiles(path, "*.csproj").Single() : path;
        var manifest = Path.Combine(directory, "dotnative.dependencies.props");
        if (!File.Exists(manifest))
            throw new FileNotFoundException("Missing dotnative.dependencies.props.", manifest);
        var dependencies = XDocument
            .Load(manifest)
            .Descendants("DotNativeDependency")
            .Select(item => new
            {
                Name = (string?)item.Attribute("Include") ?? "",
                Repository = (string?)item.Attribute("Repository") ?? "",
                Revision = (string?)item.Attribute("Revision") ?? "",
            })
            .ToArray();
        if (
            dependencies.Length == 0
            || dependencies.Select(item => item.Name).Distinct().Count() != dependencies.Length
        )
            throw new InvalidDataException(
                "Declare unique DotNativeDependency items in dotnative.dependencies.props."
            );
        foreach (var dependency in dependencies)
        {
            if (
                !Regex.IsMatch(dependency.Name, "^[A-Za-z0-9_.-]+$")
                || dependency.Name is "." or ".."
                || !DependencyRevision.IsValid(dependency.Revision)
                || !Uri.TryCreate(dependency.Repository, UriKind.Absolute, out var uri)
                || uri.Scheme != "https"
            )
                throw new InvalidDataException(
                    "Dependencies require a directory name, HTTPS Git URL and a version tag (v1.2.3) or full commit SHA."
                );
        }
        foreach (var dependency in dependencies)
        {
            var commit = dependency.Revision;
            if (!DependencyRevision.IsCommit(commit))
            {
                var remote = await CommandOutput("git", ["ls-remote", "--tags", dependency.Repository,
                    "refs/tags/" + dependency.Revision, "refs/tags/" + dependency.Revision + "^{}"]);
                if (remote.ExitCode != 0)
                    throw new IOException($"Cannot resolve release {dependency.Revision} for {dependency.Name}: {remote.Output}");
                commit = DependencyRevision.TagCommit(dependency.Revision, remote.Output);
            }
            var destination = Path.Combine(
                directory,
                ".dotnative",
                "dependencies",
                dependency.Name
            );
            if (Directory.Exists(destination))
            {
                var existing = await CommandOutput("git", ["-C", destination, "rev-parse", "HEAD"]);
                if (
                    existing.ExitCode != 0
                    || !string.Equals(
                        existing.Output.Trim(),
                        commit,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    throw new IOException(
                        $"Dependency {dependency.Name} differs from its pinned revision. Move it aside or update it manually; local work is never overwritten."
                    );
                Console.WriteLine(
                    $"[restore] {dependency.Name}: pinned checkout already exists; preserving local edits."
                );
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await Exec("git", ["clone", "--no-checkout", dependency.Repository, destination]);
            await Exec("git", ["-C", destination, "fetch", "origin", dependency.Revision]);
            await Exec("git", ["-C", destination, "checkout", "--detach", commit]);
        }
        await Exec("dotnet", ["restore", project], directory);
        return 0;
    }
}
