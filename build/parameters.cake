#load "./paths.cake"
#load "./packages.cake"
#load "./version.cake"
#load "./credentials.cake"

public class BuildParameters
{
    public FilePath Solution { get; private set; }
    public string Target { get; private set; }
    public string Configuration { get; private set; }
    public bool IsLocalBuild { get; private set; }
    public bool IsRunningOnUnix { get; private set; }
    public bool IsRunningOnWindows { get; private set; }
    public bool IsRunningOnGoCD { get; private set; }
    public bool IsRunningOnAppVeyor { get; private set; }
    public bool IsReleaseBuild { get; private set; }
    public string Repository { get; private set; }
    public BuildCredentials GitHub { get; private set; }
    public BuildCredentials Artifactory { get; private set; }
    public BuildVersion Version { get; private set; }
    public BuildPaths Paths { get; private set; }
    public BuildPackages Packages { get; private set; }
    public int BuildNumber { get; private set; }

    public bool ShouldPublish
    {
        get
        {
            return !IsLocalBuild && IsReleaseBuild;
        }
    }


    public bool ShouldPublishToArtifactory
    {
        get
        {
            return IsRunningOnGoCD && ShouldPublish;
        }
    }


    public void Initialize(ICakeContext context)
    {
        Version = BuildVersion.Calculate(context, this);

        Paths = BuildPaths.GetPaths(context, Configuration, Version.SemVersion);

        Packages = BuildPackages.GetPackages(
			Paths.Directories.ArtifactsBin,
            Paths.Directories.NugetRoot,
            Version.SemVersion,
            IsRunningOnWindows
            );
    }

    public static BuildParameters GetParameters(ICakeContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException("context");
        }

        var solution = context.GetFiles("./**/*.sln").FirstOrDefault();
        if(solution == null) 
        {
            context.Information("Unable to find solution in directory!");
            throw new InvalidOperationException("Unable to find solution in directory!");
        }

        var target = context.Argument("target", "Default");
        var buildSystem = context.BuildSystem();

        var buildNumber = 0;
        if(buildSystem.GoCD.IsRunningOnGoCD)
            buildNumber = buildSystem.GoCD.Environment.Pipeline.Counter;
        if(buildSystem.AppVeyor.IsRunningOnAppVeyor)
            buildNumber = buildSystem.AppVeyor.Environment.Build.Number;


        return new BuildParameters {
            Solution = solution,
            Target = target,
            Configuration = context.Argument("configuration", "Release"),
            IsLocalBuild = buildSystem.IsLocalBuild,
            IsRunningOnUnix = context.IsRunningOnUnix(),
            IsRunningOnWindows = context.IsRunningOnWindows(),
            IsRunningOnGoCD = buildSystem.GoCD.IsRunningOnGoCD,
            IsRunningOnAppVeyor = buildSystem.AppVeyor.IsRunningOnAppVeyor,
            GitHub = BuildCredentials.GetGitHubCredentials(context),
            Artifactory = BuildCredentials.GetArtifactoryCredentials(context, buildSystem.IsLocalBuild),
            IsReleaseBuild = IsReleasing(target),
            BuildNumber = buildNumber
        };
    }


    private static bool IsReleasing(string target)
    {
        var targets = new [] { "GoCD", "AppVeyor", "Publish", "Publish-NuGet" };
        return targets.Any(t => StringComparer.OrdinalIgnoreCase.Equals(t, target));
    }

}
