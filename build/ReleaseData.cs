public record ReleaseData(
    DirectoryPath ArtifactPath,
    string Version
)
{
    public string GitHubNuGetApiKey { get; } = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? throw new InvalidOperationException("GITHUB_TOKEN environment variable is not set.");
    public IList<FilePath> ArtifactPaths { get; } = [];
}