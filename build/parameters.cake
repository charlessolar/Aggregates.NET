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
    public bool IsRunningOnVSTS { get; private set; }
    public bool IsRunningOnAppVeyor { get; private set; }
    public bool IsRunningOnGitHub { get; private set; }
    public bool IsReleaseBuild { get; private set; }
    public string Repository { get; private set; }
    public BuildCredentials GitHub { get; private set; }
    public BuildCredentials Artifactory { get; private set; }
    public BuildVersion Version { get; private set; }
    public BuildPaths Paths { get; private set; }
    public BuildPackages Packages { get; private set; }
    public int BuildNumber { get; private set; }
    public string Branch { get; private set; }
    public bool IsMaster { get; private set; }
    public bool IsPullRequest { get; private set; }

    public bool ShouldPublish
    {
        get
        {
            return !IsLocalBuild && IsReleaseBuild && IsMaster && !IsPullRequest;
        }
    }


    public bool ShouldPublishToArtifactory
    {
        get
        {
            // Can publish pre-release to artifactory
            return IsRunningOnVSTS  && !IsLocalBuild && IsReleaseBuild && !IsPullRequest;
        }
    }


    public void Initialize(ICakeContext context)
    {
        Version = BuildVersion.Calculate(context, this);

        Paths = BuildPaths.GetPaths(context, Configuration, Version.SemVersion);

        Packages = BuildPackages.GetPackages(
            context,
            IsRunningOnWindows,
            Version,
            Paths.Directories.ArtifactsBin,
            Paths.Directories.NugetRoot,
            Paths.Files.Projects
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

        var isVSTS = buildSystem.TFBuild.IsRunningOnVSTS || buildSystem.TFBuild.IsRunningOnTFS;

        var repository = "";

        var buildNumber = 0;
        var branch = "";
        var pr = false;
        if(buildSystem.AppVeyor.IsRunningOnAppVeyor) {
            buildNumber = buildSystem.AppVeyor.Environment.Build.Number;
            branch = buildSystem.AppVeyor.Environment.Repository.Branch;
            pr = buildSystem.AppVeyor.Environment.PullRequest.IsPullRequest;
            repository = "https://github.com/volak/Aggregates.NET";
        }
        if(isVSTS) {
            buildNumber = buildSystem.TFBuild.Environment.Build.Id;
            branch = buildSystem.TFBuild.Environment.Repository.Branch;
            repository = context.Environment.GetEnvironmentVariable("BUILD_REPOSITORY_URI");
        }
        if(buildSystem.GitHubActions.IsRunningOnGitHubActions) {
            
            buildNumber = context.Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
            branch = buildSystem.GitHubActions.Environment.Workflow.Ref;
            repository = buildSystem.GitHubActions.Environment.Workflow.Repository;
        }


        return new BuildParameters {
            Solution = solution,
            Target = target,
            Configuration = context.Argument("configuration", "Release"),
            IsLocalBuild = buildSystem.IsLocalBuild,
            IsRunningOnUnix = context.IsRunningOnUnix(),
            IsRunningOnWindows = context.IsRunningOnWindows(),
            IsRunningOnAppVeyor = buildSystem.AppVeyor.IsRunningOnAppVeyor,
            IsRunningOnVSTS = isVSTS,
            IsRunningOnGitHub = buildSystem.GitHubActions.IsRunningOnGitHubActions,
            Repository = repository,
            GitHub = BuildCredentials.GetGitHubCredentials(context),
            Artifactory = BuildCredentials.GetArtifactoryCredentials(context, buildSystem.IsLocalBuild),
            IsReleaseBuild = IsReleasing(target),
            BuildNumber = buildNumber,
            Branch = branch,
            IsMaster = StringComparer.OrdinalIgnoreCase.Equals("master", branch),
            IsPullRequest = pr
        };
    }


    private static bool IsReleasing(string target)
    {
        var targets = new [] { "AppVeyor", "GitHub", "VSTS-Publish", "Publish", "Publish-NuGet" };
        return targets.Any(t => StringComparer.OrdinalIgnoreCase.Equals(t, target));
    }

}
