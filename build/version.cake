public class BuildVersion
{
    public string Version { get; private set; }
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
            OutputType = GitVersionOutput.Json
        });

        string version = gitversion.MajorMinorPatch;
        string semVersion = string.Concat(version, ".", gitversion.BuildMetaData);
        string milestone = string.Concat("v", version);
        string sha = gitversion.Sha;

        context.Information("Calculated Semantic Version: {0} sha: {1}", semVersion, sha.Substring(0,8));
        context.Information("Informational Version: {0}", gitversion.InformationalVersion);

        return new BuildVersion
        {
            Version = version,
            SemVersion = semVersion,
            Milestone = milestone,
            Sha = sha,
            CakeVersion = cakeVersion
        };
    }


}