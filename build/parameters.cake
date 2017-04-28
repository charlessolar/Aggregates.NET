#load "./paths.cake"
#load "./packages.cake"
#load "./version.cake"
#load "./credentials.cake"

public class BuildParameters
{
    public string Target { get; private set; }
    public string Configuration { get; private set; }
    public bool IsLocalBuild { get; private set; }
    public bool IsRunningOnUnix { get; private set; }
    public bool IsRunningOnWindows { get; private set; }
    public bool IsRunningOnAppVeyor { get; private set; }
    public bool IsPullRequest { get; private set; }
    public bool IsMainRepo { get; private set; }
    public bool IsMasterBranch { get; private set; }
    public bool IsTagged { get; private set; }
    public bool IsReleaseBuild { get; private set; }
    public bool SkipGitVersion { get; private set; }
    public BuildCredentials GitHub { get; private set; }
    public BuildVersion Version { get; private set; }
    public BuildPaths Paths { get; private set; }
    public BuildPackages Packages { get; private set; }

    public bool ShouldPublish
    {
        get
        {
            return !IsLocalBuild && !IsPullRequest && IsMainRepo
                && IsMasterBranch && IsTagged;
        }
    }



    public void Initialize(ICakeContext context)
    {
        Version = BuildVersion.Calculate(context, this);

        Paths = BuildPaths.GetPaths(context, Configuration, Version.SemVersion);

        Packages = BuildPackages.GetPackages(
			Paths.Directories.ArtifactsBin,
            Paths.Directories.NugetRoot,
            Version.SemVersion
            );
    }

    public static BuildParameters GetParameters(ICakeContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException("context");
        }

        var target = context.Argument("target", "Default");
        var buildSystem = context.BuildSystem();

        return new BuildParameters {
            Target = target,
            Configuration = context.Argument("configuration", "Release"),
            IsLocalBuild = buildSystem.IsLocalBuild,
            IsRunningOnUnix = context.IsRunningOnUnix(),
            IsRunningOnWindows = context.IsRunningOnWindows(),
            IsRunningOnAppVeyor = buildSystem.AppVeyor.IsRunningOnAppVeyor,
            IsPullRequest = buildSystem.AppVeyor.Environment.PullRequest.IsPullRequest,
            IsMainRepo = StringComparer.OrdinalIgnoreCase.Equals("volak/Aggregates.NET", buildSystem.AppVeyor.Environment.Repository.Name),
            IsMasterBranch = StringComparer.OrdinalIgnoreCase.Equals("master", buildSystem.AppVeyor.Environment.Repository.Branch),
            IsTagged = IsBuildTagged(buildSystem),
            GitHub = BuildCredentials.GetGitHubCredentials(context),
            IsReleaseBuild = IsReleasing(target),
            SkipGitVersion = StringComparer.OrdinalIgnoreCase.Equals("True", context.EnvironmentVariable("SKIP_GITVERSION"))
        };
    }

    private static bool IsBuildTagged(BuildSystem buildSystem)
    {
        return buildSystem.AppVeyor.Environment.Repository.Tag.IsTag
            && !string.IsNullOrWhiteSpace(buildSystem.AppVeyor.Environment.Repository.Tag.Name);
    }

    private static bool IsReleasing(string target)
    {
        var targets = new [] { "Publish", "Publish-NuGet" };
        return targets.Any(t => StringComparer.OrdinalIgnoreCase.Equals(t, target));
    }

}
