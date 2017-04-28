public class BuildPaths
{
	public BuildFiles Files { get; private set; }
    public BuildDirectories Directories { get; private set; }

    public static BuildPaths GetPaths(
        ICakeContext context,
        string configuration,
        string semVersion
        )
    {
        if (context == null)
        {
            throw new ArgumentNullException("context");
        }
        if (string.IsNullOrEmpty(configuration))
        {
            throw new ArgumentNullException("configuration");
        }
        if (string.IsNullOrEmpty(semVersion))
        {
            throw new ArgumentNullException("semVersion");
        }

		var directories = context.GetDirectories("./src/**/bin/" + configuration);

        var artifactsDir = (DirectoryPath)(context.Directory("./artifacts") + context.Directory("v" + semVersion));
        var artifactsBinDir = artifactsDir.Combine("bin");
        var testResultsDir = artifactsDir.Combine("test-results");
        var nugetRoot = artifactsDir.Combine("nuget");

        var repoFilesPaths = new FilePath[] {
            "LICENSE",
            "README.md"
        };

		var zipBinary = artifactsDir.CombineWithFilePath("Aggregates.Net-net46-v" + semVersion + ".zip");
        var zipSource = artifactsDir.CombineWithFilePath("Aggregates.Net-source-v" + semVersion + ".zip");

        // Directories
        var buildDirectories = new BuildDirectories(
            directories,
            nugetRoot,
            artifactsBinDir);
		// Files
		var buildFiles = new BuildFiles(
			zipBinary,
			zipSource);

        return new BuildPaths
        {
			Files = buildFiles,
            Directories = buildDirectories
        };
    }
}


public class BuildDirectories
{
    public IEnumerable<DirectoryPath> ArtifactSources { get; private set; }
    public DirectoryPath NugetRoot { get; private set; }
    public DirectoryPath ArtifactsBin { get; private set; }
    public IEnumerable<DirectoryPath> ToClean { get; private set; }

    public BuildDirectories(
        IEnumerable<DirectoryPath> artifactSources,
        DirectoryPath nugetRoot,
        DirectoryPath artifactsBinDir
        )
    {
        ArtifactSources = artifactSources;
        NugetRoot = nugetRoot;
        ArtifactsBin = artifactsBinDir;
        ToClean = new[] {
            ArtifactsBin,
            NugetRoot
        };
    }
}

public class BuildFiles
{

	public FilePath ZipBinaries { get; private set; }
    public FilePath ZipSource { get; private set; }
    
	public BuildFiles(
		FilePath binaries,
		FilePath sources)
	{
		ZipBinaries = binaries;
		ZipSource = sources;
	}

}