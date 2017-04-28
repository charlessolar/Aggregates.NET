public class BuildCredentials
{
    public string UserName { get; private set; }
    public string Password { get; private set; }

    public BuildCredentials(string userName, string password)
    {
        UserName = userName;
        Password = password;
    }

    public static BuildCredentials GetGitHubCredentials(ICakeContext context)
    {
        return new BuildCredentials(
            context.EnvironmentVariable("GITHUB_USERNAME"),
            context.EnvironmentVariable("GITHUB_PASSWORD"));
    }
}