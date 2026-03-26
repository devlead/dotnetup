#:sdk Cake.Sdk@6.1.1
#:property IncludeAdditionalFiles=./build/*.cs

/*****************************
 * Setup
 *****************************/
Setup(
    static context => {
        const string author = "devlead";
        var isMainBranch = GitHubActions.IsRunningOnGitHubActions
                            && GitHubActions.Environment.Workflow.RepositoryOwner == author && GitHubActions.Environment.Workflow.Ref == "refs/heads/main";

        var buildDate = DateTime.UtcNow;
        var runNumber = GitHubActions.IsRunningOnGitHubActions
                            ? GitHubActions.Environment.Workflow.RunNumber
                            : 1;

        var suffix = isMainBranch ? string.Empty : $"-{(short)((buildDate - buildDate.Date).TotalSeconds/3):00000}";

        var version = FormattableString
                    .Invariant($"{buildDate:yyyy.M.d}.{runNumber}{suffix}");

        var artifactsPath = context
                            .MakeAbsolute(context.Directory("./artifacts"));

        var branchName = GitHubActions.IsRunningOnGitHubActions
            ? GitHubActions.Environment.Workflow.Ref
            : "local";

        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        var artifactName = $"dotnetup_{context.Environment.Platform.Family:F}_{architecture}".ToLowerInvariant();

        static string ToCheckmark(bool value) => value ? "[green]✓[/]" : "[red]✗[/]";

        var data = new BuildData(
            Version: version,
            IsMainBranch: isMainBranch,
            SdkPath: MakeAbsolute(Directory("SDK")),
            MSBuildSettings: new DotNetMSBuildSettings()
                                .SetConfiguration("Release")
                                .SetVersion(version)
                                .WithProperty("Copyright", $"Mattias Karlsson © {DateTime.UtcNow.Year}")
                                .WithProperty("Authors", author)
                                .WithProperty("Company", author)
                                .WithProperty("PackageTags", "Cake Script Build Slack cake-addin addin cake-build")
                                .WithProperty("PackageLicenseExpression", "MIT")
                                .WithProperty("ContinuousIntegrationBuild", GitHubActions.IsRunningOnGitHubActions ? "true" : "false")
                                .WithProperty("EmbedUntrackedSources", "true"),
            ArtifactsPath: artifactsPath,
            ArtifactName: artifactName,
            OutputPath: artifactsPath.Combine(version));

        AnsiConsole.Write(
            new Table()
                .RoundedBorder()
                .HideHeaders()
                .ShowRowSeparators()
                .AddColumn("Property")
                .AddColumn("Value")
                .AddRow("Build Date", buildDate.ToString("yyyy-MM-dd HH:mm:ss UTC"))
                .AddRow("Build System", BuildSystem.Provider.ToString("F"))
                .AddRow("Branch", branchName)
                .AddRow("Main", ToCheckmark(data.IsMainBranch))
                .AddRow("Version", data.Version)
                .AddRow("Artifact Name", data.ArtifactName)
                .AddRow("Binary Output Path", data.BinaryOutputPath.FullPath));

        return data;
    });

/*****************************
 * Tasks
 *****************************/
Task(nameof(DotNetBuildServerShutdown))
    .Does<BuildData>(static (context, data) =>
    {
       DotNetBuildServerShutdown();
    })
.Then("Clean")
    .Does<BuildData>(static (context, data) =>
    {
        CleanDirectories(data.DirectoryPathsToClean);

        if (DirectoryExists(data.SdkPath))
        {
            DeleteDirectory(
                data.SdkPath,
                new DeleteDirectorySettings { 
                    Recursive = true,
                    Force = true
                }
            );
        }
    })
.Then("Clone-SDK")
    .Does<BuildData>(static (context, data) =>
    {
        var gitSettings = new CommandSettings
        {
            ToolExecutableNames = ["git", "git.exe"],
            ToolName = "Git"
        };
        Command(
            gitSettings,
            arguments: new ProcessArgumentBuilder()
                            .Append("clone")
                            .Append("--branch release/dnup")
                            .Append("--depth 1")
                            .Append("--filter=blob:none")
                            .Append("https://github.com/dotnet/sdk.git")
                            .AppendQuoted(data.SdkPath.FullPath)
        );
    })
.Then("Publish-DotNetUp")
    .Does<BuildData>(static (context, data) =>
    {
       DotNetPublish(
            data.DotNetUpSourcePath.FullPath,
            new DotNetPublishSettings {
                OutputDirectory = data.BinaryOutputPath.FullPath,
                MSBuildSettings = data.MSBuildSettings,
                SelfContained = true,
                PublishSingleFile = true,
                PublishReadyToRun = true,
                ArgumentCustomization = args 
                                            => args
                                                .Append("--use-current-runtime")
            }
        );
    })
.Then("Store-BuildData")
    .Does<BuildData>(static (context, data) =>
    {
        Information($"Storing build data to {data.BuildDataFilePath.FullPath}");
        var file = Context.FileSystem.GetFile(data.BuildDataFilePath);
        using var stream = file.OpenWrite();
        JsonSerializer.Serialize(
            stream,
            data,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }
        );
    })
.Then("Inspect-Output")
    .Does<BuildData>(static (context, data) =>
    {
        Information("Inspecting output:");
        foreach(var file in GetFiles($"{data.OutputPath}/**/*"))
        {
            Information($"{file.FullPath}");
        };
    })
.Then("Integration-Test-DotNetUp")
    .Does<BuildData>(static (context, data) =>
    {

        Command(
            new CommandSettings
            {
                ToolExecutableNames = ["dotnetup", "dotnetup.exe"],
                WorkingDirectory = data.BinaryOutputPath,
                ToolPath = data.DotNetUpPath,
                ToolName = "dotnetup"
            },
            arguments: "--info"
        );
    })
    .Default()
.Then("Upload-Artifacts")
    .WithCriteria(BuildSystem.IsRunningOnGitHubActions, nameof(BuildSystem.IsRunningOnGitHubActions))
    .Does<BuildData>(
        static (context, data)
            => GitHubActions
                .Commands
                .UploadArtifact(data.OutputPath, data.ArtifactName)
    )
.Then("GitHub-Actions")
    .Run();
