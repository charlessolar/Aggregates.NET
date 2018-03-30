public class BuildPackages
{
    public IEnumerable<BuildPackage> Nuget { get; private set; }
    public IEnumerable<BuildDocker> Images { get; private set; }
    public IEnumerable<BuildBinary> Binaries { get; private set; }


    public static BuildPackages GetPackages(
        ICakeContext context,
        bool windows,
        BuildVersion version,
        DirectoryPath artifactsDir,
        DirectoryPath nugetDir,
        IEnumerable<ProjectInfo> projects)
    {

		var nugets = projects.Where(x => x.OutputType == "Library").Select(project =>
            new BuildPackage(
                id: project.AssemblyName,
                projectPath: project.ProjectFile.FullPath,
                packagePath: nugetDir.CombineWithFilePath(string.Concat(project.AssemblyName, ".", version.NuGet, ".nupkg"))
            ));

        return new BuildPackages { 
			Nuget = nugets
		};
    }

}

public class BuildPackage
{
    public string Id { get; private set; }
    public FilePath ProjectPath { get; private set; }
    public FilePath PackagePath { get; private set; }

    public BuildPackage(
        string id,
        FilePath projectPath,
        FilePath packagePath)
    {
        Id = id;
        ProjectPath = projectPath;
        PackagePath = packagePath;
    }
}

public class BuildDocker
{
    public string Id { get; private set; }
    public DirectoryPath BaseDir { get; private set; }
    public DockerImageBuildSettings Settings { get; private set; }
    public FilePath PackagePath { get; private set; }

    public BuildDocker(
        string id,
        DirectoryPath baseDir,
        DockerImageBuildSettings settings,
        FilePath packagePath)
    {
        Id = id;
        BaseDir = baseDir;
        Settings = settings;
        PackagePath = packagePath;
    }
}

public class BuildBinary
{
    public string Id { get; private set; }
    public DirectoryPath BaseDir { get; private set; }
    public FilePath PackagePath { get; private set; }

    public BuildBinary(
        string id,
        DirectoryPath baseDir,
        FilePath packagePath)
    {
        Id = id;
        BaseDir = baseDir;
        PackagePath = packagePath;
    }
}
