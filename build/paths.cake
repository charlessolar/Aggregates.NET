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

        var projects = context.GetFiles("./src/**/*.csproj");
        var projectInfos = new List<ProjectInfo>();

        var binDirs = new List<DirectoryPath>();
        foreach(var project in projects)
        {
            var info = context.ParseProject(project, configuration);
            var projectDir = project.GetDirectory();

            var output = info.OutputType;
            var binDir = info.OutputPath;
            Func<IEnumerable<FilePath>> getBinaries = () => {
                return 
                        context.GetFiles(binDir + "/*");
            };

            if(context.FileExists(projectDir.CombineWithFilePath("./Dockerfile")))
            {
                output = "Docker";

                getBinaries = () => {
                    return 
                        context.GetFiles(binDir + "/*")
                        .Concat(context.GetFiles(projectDir + "/Dockerfile*"));
                };
            }
            if(info.IsWebApplication()) 
            {
                output = "Web";

                binDir = project.GetDirectory().Combine(context.Directory("bin"));
                getBinaries = () => {
                    return 
                        context.GetFiles(binDir + "/*")
                        .Concat(context.GetFiles(projectDir + "/*.asax"))
                        .Concat(context.GetFiles(projectDir + "/*.config"))
                        .Concat(context.GetFiles(projectDir + "/*.sh"))
                        .Concat(context.GetFiles(projectDir + "/Dockerfile*"));
                };
            }
            if(info.AssemblyName.EndsWith("Tests")) 
            {
                output = "Test";
            }

            binDirs.Add(binDir);

            context.Information("Discovered project {0} output type {1}", info.AssemblyName, output);
            var filename = project.GetFilename().FullPath;
            projectInfos.Add(new ProjectInfo(
                filename.Substring(0, filename.Length-7), 
                info.AssemblyName, 
                output, 
                project, 
                getBinaries)
                );
        }

        var artifactsDir = (DirectoryPath)context.Directory("./artifacts");
        var artifactsBinDir = artifactsDir.Combine("bin");
        var testResultsDir = artifactsDir.Combine("test-reports");
        var nugetRoot = artifactsDir.Combine("nuget");

        var zipBinary = artifactsDir.CombineWithFilePath("Build-v" + semVersion + ".zip");
        var zipSource = artifactsDir.CombineWithFilePath("Build-source-v" + semVersion + ".zip");

        // Directories
        var buildDirectories = new BuildDirectories(
            nugetRoot,
            artifactsDir,
            artifactsBinDir,
            testResultsDir,
            binDirs);
        // Files
        var buildFiles = new BuildFiles(
            projectInfos,
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
    public DirectoryPath NugetRoot { get; private set; }
    public DirectoryPath ArtifactsDir { get; private set; }
    public DirectoryPath ArtifactsBin { get; private set; }
    public DirectoryPath TestResultsDir { get; private set; }
    public IEnumerable<DirectoryPath> ToClean { get; private set; }

    public BuildDirectories(
        DirectoryPath nugetRoot,
        DirectoryPath artifactsDir,
        DirectoryPath artifactsBinDir,
        DirectoryPath testResultsDir,
        IEnumerable<DirectoryPath> binDirs
        )
    {
        NugetRoot = nugetRoot;
        ArtifactsBin = artifactsBinDir;
        ArtifactsDir = artifactsDir;
        TestResultsDir = testResultsDir;
        ToClean = binDirs.Concat(new[] {
            ArtifactsDir,
            ArtifactsBin,
            NugetRoot
        });
    }
}

public class BuildFiles
{
    public IEnumerable<ProjectInfo> Projects { get; private set; }
    public FilePath ZipBinaries { get; private set; }
    public FilePath ZipSource { get; private set; }
    
    public BuildFiles(
        IEnumerable<ProjectInfo> projects,
        FilePath binaries,
        FilePath sources
        )
    {
        Projects = projects;
        ZipBinaries = binaries;
        ZipSource = sources;
    }

}

public class ProjectInfo
{
    public string ProjectName { get; private set; }
    public string AssemblyName { get; private set; }
    public string OutputType { get; private set; }
    public FilePath ProjectFile { get; private set; }
    public Func<IEnumerable<FilePath>> GetBinaries { get; private set; }

    public ProjectInfo(
        string projectName,
        string assemblyName,
        string outputType,
        FilePath projectFile,
        Func<IEnumerable<FilePath>> getBinaries
    )
    {
        ProjectName = projectName;
        AssemblyName = assemblyName;
        OutputType = outputType;
        ProjectFile = projectFile;
        GetBinaries = getBinaries;
    }
}
