#:sdk Cake.Sdk@6.1.1
#:property IncludeAdditionalFiles=./build/*.cs

Setup(static context =>
{
    var artifactPath = MakeAbsolute(
        Directory(
            EnvironmentVariable("DOTNETUP_ARTIFACTPATH") ?? throw new InvalidOperationException("DOTNETUP_ARTIFACTPATH environment variable is not set.")
        )
    );

    var buildDataPath = GetFiles($"{artifactPath}/**/BuildData.json")
                            .FirstOrDefault()
                            ??
                            throw new FileNotFoundException($"BuildData.json not found in {artifactPath}.");
    
    var buildDataFile = Context.FileSystem.GetFile(buildDataPath);
    using var stream = buildDataFile.OpenRead();
    var buildData = JsonSerializer.Deserialize<JsonDocument>(stream)
                        ?? throw new InvalidOperationException("Failed to deserialize BuildData.json.");

    var releaseData = new ReleaseData(
        ArtifactPath: artifactPath,
        Version: buildData.RootElement.GetProperty("Version").GetString() ?? throw new InvalidOperationException("Version not found in BuildData.json.")
        );

      AnsiConsole.Write(
            new Table()
                .RoundedBorder()
                .HideHeaders()
                .ShowRowSeparators()
                .AddColumn("Property")
                .AddColumn("Value")
                .AddRow("Version", releaseData.Version)
                .AddRow("Artifact Path", artifactPath.FullPath));

    return releaseData;                 
});

Task("Inspect-Release")
    .Does<ReleaseData>(static (context, releaseData) =>
    {
        Information($"Inspecting {releaseData.ArtifactPath}:");
        foreach(var file in GetFiles($"{releaseData.ArtifactPath}/**/*"))
        {
            Information($"{file.FullPath}");
        }
    })
.Then("Zip-Artifacts")
    .Does<ReleaseData>(static (context, releaseData) =>
    {
        foreach(var dir in Context
            .FileSystem
            .GetDirectory(releaseData.ArtifactPath)
            .GetDirectories("*", SearchScope.Current))
        {
            var zipPath = File($"{dir.Path}.zip");
            Zip(dir.Path, zipPath);
            releaseData.ArtifactPaths.Add(zipPath);
        }
    })
.Default()
    .Then("Create-GitHub-Release")
    .Does<ReleaseData>(
        static (context, data) => context
            .Command(
                new CommandSettings {
                    ToolName = "GitHub CLI",
                    ToolExecutableNames = ["gh.exe", "gh"],
                    EnvironmentVariables = { { "GH_TOKEN", data.GitHubNuGetApiKey } }
                },
                new ProcessArgumentBuilder()
                    .Append("release")
                    .Append("create")
                    .Append(data.Version)
                    .AppendSwitchQuoted("--title", data.Version)
                    .Append("--generate-notes")
                    .Append(string.Join(
                        ' ',
                        data.
                            ArtifactPaths
                            .Select(path => path.FullPath.Quote())
                        ))

            )
    )
.Then("GitHub-Actions-Release")
.Run();