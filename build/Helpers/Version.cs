using Cake.Common.Tools.GitVersion;
using Cake.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Build.Helpers
{
    public class BuildVersion
    {
        public string Version { get; private set; }
        public string NuGet { get; private set; }
        public string SemVersion { get; private set; }
        public string Milestone { get; private set; }
        public string Sha { get; private set; }
        public string CakeVersion { get; private set; }

        public static BuildVersion Calculate(BuildParameters context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            
            var cakeVersion = typeof(ICakeContext).Assembly.GetName().Version.ToString();

            string version = null;
            string semVersion = null;
            string milestone = null;
            string sha = null;
            string informational = null;

            if (!context.IsLocalBuild || context.IsReleaseBuild)
            {
                context.GitVersion(new GitVersionSettings
                {
                    UpdateAssemblyInfoFilePath = "./src/SharedAssemblyInfo.cs",
                    UpdateAssemblyInfo = true,
                    OutputType = GitVersionOutput.BuildServer,
                    RepositoryPath = context.Solution.GetDirectory()
                });

            }

            // Note: nuget doesn't really support a build version SemVer, we are kind of hacking it here
            // its not really supported in GitVersion either.
            // When it comes time to actually have pull requests and different branches
            // a CI strategy will have to be developed so that nuget packages are properly
            // labeled pre-release "v0.1.0-feature-test.3" and that upstream dependencies are updated
            // to pull the pre-release version.
            // Currently the setup is nuget restore will restore the latest version up to v0.2 for example
            // including each new build we push to artifactory

            var gitversion = context.GitVersion(new GitVersionSettings
            {
                OutputType = GitVersionOutput.Json,
                RepositoryPath = context.Solution.GetDirectory()
            });

            version = string.Concat(gitversion.Major, ".", gitversion.Minor);
            semVersion = string.Concat(version, ".", gitversion.BuildMetaData, ".", context.BuildNumber);
            milestone = string.Concat("v", version);
            sha = gitversion.Sha;
            informational = gitversion.InformationalVersion;

            string nuget = semVersion;
            // tag nont master branches with pre-release 
            // gitversion in the future will support something similar
            if (!context.IsMaster && !context.IsLocalBuild)
                nuget += "-" + context.Branch;

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
}
