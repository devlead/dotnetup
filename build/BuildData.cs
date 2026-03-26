public record BuildData(
    string Version,
    bool IsMainBranch,
    DirectoryPath SdkPath,
    DotNetMSBuildSettings MSBuildSettings,
    DirectoryPath ArtifactsPath,
    string ArtifactName,
    DirectoryPath OutputPath
    )
{
    public DirectoryPath BinaryOutputPath { get; } = OutputPath.Combine("bin");
    public DirectoryPath DotNetUpSourcePath { get; } = SdkPath.Combine("src").Combine("Installer").Combine("dotnetup");
    public FilePath DotNetUpPath { get; } = OutputPath
                                            .Combine("bin")
                                            .CombineWithFilePath(
                                                IsRunningOnWindows()
                                                    ? "dotnetup.exe"
                                                    : "dotnetup"
                                            );

    public FilePath BuildDataFilePath { get; } = OutputPath
                                                    .CombineWithFilePath("BuildData.json");

    public ICollection<DirectoryPath> DirectoryPathsToClean = [
        ArtifactsPath,
        OutputPath
    ];
}