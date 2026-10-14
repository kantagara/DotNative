using System.Text.RegularExpressions;

internal static class DependencyRevision
{
    public static bool IsCommit(string value) => Regex.IsMatch(value, "^[a-fA-F0-9]{40}$");

    public static bool IsValid(string value) => IsCommit(value)
        || Regex.IsMatch(value, @"^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$");

    public static string TagCommit(string tag, string remoteOutput)
    {
        if (!IsValid(tag) || IsCommit(tag)) throw new ArgumentException("Expected a version tag.", nameof(tag));
        var reference = "refs/tags/" + tag;
        var refs = remoteOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split('\t'))
            .Where(parts => parts.Length == 2 && IsCommit(parts[0]))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        return refs.GetValueOrDefault(reference + "^{}") ?? refs.GetValueOrDefault(reference)
            ?? throw new InvalidDataException($"Release tag {tag} does not exist in the dependency repository.");
    }
}
