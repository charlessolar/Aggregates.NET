public class BuildVersion
{
    public string Version { get; private set; }
    public string NuGet { get; private set; }
    public string SemVersion { get; private set; }
    public string Milestone { get; private set; }
    public string Sha { get; private set; }
    public string CakeVersion { get; private set; }

    public static BuildVersion Calculate(ICakeContext context, BuildParameters parameters)
    {
        if (context == null)
        {
            throw new ArgumentNullException("context");
        }
        var cakeVersion = typeof(ICakeContext).Assembly.GetName().Version.ToString();

        var gitversion = context.GitVersion(new GitVersionSettings {
            UpdateAssemblyInfoFilePath = "./src/SharedAssemblyInfo.cs",
            UpdateAssemblyInfo = !parameters.IsLocalBuild,
            OutputType = GitVersionOutput.Json,
        });

        string version = string.Concat(gitversion.Major, ".", gitversion.Minor);
        string semVersion = string.Concat(version, ".", gitversion.BuildMetaData, ".", parameters.BuildNumber);
        string milestone = string.Concat("v", version);
        string sha = gitversion.Sha;

        string nuget = semVersion;
        // tag nont master branches with pre-release 
        // gitversion in the future will support something similar
        if(!parameters.IsMaster && !parameters.IsLocalBuild) 
            nuget += "-" + parameters.Branch;

        return new BuildVersion
        {
            Version = version,
            NuGet = nuget,
            SemVersion = semVersion,
            Milestone = milestone,
            Sha = sha,
            CakeVersion = cakeVersion
        };
    }


}