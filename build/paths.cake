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
        
        var projects = context.GetFiles("./**/*.csproj");
        var projectInfos = new List<ProjectInfo>();

        var binDirs = new List<DirectoryPath>();
        foreach(var project in projects)
        {
            var info = context.ParseProject(project);
            var projectDir = project.GetDirectory();

            var output = info.OutputType;
            var binDir = project.GetDirectory().Combine(context.Directory("bin")).Combine(context.Directory(configuration));
            Func<IEnumerable<FilePath>> getBinaries = () => {
                return 
                        context.GetFiles(binDir + "/*");
            };

            if(info.Files.Any(x => x.FilePath.GetFilename().ToString() == "Dockerfile"))
            {
                output = "Docker";

                getBinaries = () => {
                    return 
                        context.GetFiles(binDir + "/*")
                        .Concat(context.GetFiles(projectDir + "/Dockerfile*"));
                };
            }
            if(info.References.Any(x => x.Include == "System.Web")) 
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

            binDirs.Add(binDir);

            var filename = project.GetFilename().FullPath;
            projectInfos.Add(new ProjectInfo(filename.Substring(0,filename.Length-7), info.AssemblyName, output, project, getBinaries));
        }

        var artifactsDir = (DirectoryPath)context.Directory("./artifacts");
        var artifactsBinDir = artifactsDir.Combine("bin");
        var testResultsDir = artifactsDir.Combine("test-reports");
        var nugetRoot = artifactsDir.Combine("nuget");

        var zipBinary = artifactsDir.CombineWithFilePath("Build-net46-v" + semVersion + ".zip");
        var zipSource = artifactsDir.CombineWithFilePath("Build-source-v" + semVersion + ".zip");

        // Directories
        var buildDirectories = new BuildDirectories(
            nugetRoot,
            artifactsDir,
            artifactsBinDir,
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
    public IEnumerable<DirectoryPath> ToClean { get; private set; }

    public BuildDirectories(
        DirectoryPath nugetRoot,
        DirectoryPath artifactsDir,
        DirectoryPath artifactsBinDir,
        IEnumerable<DirectoryPath> binDirs
        )
    {
        NugetRoot = nugetRoot;
        ArtifactsBin = artifactsBinDir;
        ArtifactsDir = artifactsDir;
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
        FilePath sources)
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
