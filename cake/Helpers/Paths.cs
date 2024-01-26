using Cake.Common.IO;
using Cake.Common.Solution;
using Cake.Common.Solution.Project;
using Cake.Core;
using Cake.Core.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cake.Incubator.Project;
using Cake.Core.Diagnostics;

namespace Build.Helpers
{
    public class BuildPaths
    {
        public BuildFiles Files { get; private set; }
        public BuildDirectories Directories { get; private set; }

        public static BuildPaths GetPaths(
            BuildParameters context,
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

            var solution = context.ParseSolution(context.Solution);

            // solution projects contains solution folders, filter those out
            var projects = solution.Projects.Where(x => x.Path.ToString().EndsWith("csproj")).Select(x => x.Path);
            var projectInfos = new List<ProjectInfo>();

            var binDirs = new List<DirectoryPath>();
            foreach (var project in projects)
            {
                var info = context.ParseProject(project, configuration);
                var projectDir = project.GetDirectory();

                var output = info.OutputType;
                // Multi-targeting support take output path of 
                // src/Presentation/ServiceStack/bin/Release/netstandard2.0
                // and turn it into 
                // src/Presentation/ServiceStack/bin/Release/
                // so that when we clean/zip we get ALLL builds
                var binDir = info.OutputPaths[0].Combine("..");

                // dont build "build" projects
                if (info.HasPackage("Cake.Core") || info.HasPackage("Cake.Frosting"))
                    continue;

                /*
                 * If you see this error in build:
                 * 
                 * Exception: NullReferenceException: Object reference not set to an instance of an object.
                   at Cake.Incubator.StringExtensions.StringExtensions.EqualsIgnoreCase(String source, String value)
                   at Cake.Incubator.Project.ProjectParserExtensions.<>c.<IsDotNetCliTestProject>b__7_0(PackageReference x)
                   at System.Linq.Enumerable.Any[TSource](IEnumerable`1 source, Func`2 predicate)
                   at Cake.Incubator.Project.ProjectParserExtensions.IsDotNetCliTestProject(CustomProjectParserResult projectParserResult)
                   at Cake.Incubator.Project.ProjectParserExtensions.IsTestProject(CustomProjectParserResult projectParserResult)
                   at Build.Helpers.BuildPaths.GetPaths(BuildParameters context, String configuration, String semVersion) in D:\projects\Aggregates.NET\cake\Helpers\Paths.cs:line 78
                   at Build.Helpers.BuildParameters.Setup() in D:\projects\Aggregates.NET\cake\Helpers\Parameters.cs:line 194
                   at Build.BuildLifetime.Setup(BuildParameters parameters, ISetupContext context) in D:\projects\Aggregates.NET\cake\Program.cs:line 52
                   at Cake.Frosting.FrostingLifetime`1.Cake.Frosting.IFrostingSetup.Setup(ICakeContext context, ISetupContext info)
                   at Cake.Frosting.Internal.FrostingEngine`1.<ConfigureLifetime>b__14_0(ISetupContext info)
                   at Cake.Core.DefaultExecutionStrategy.PerformSetup(Action`1 action, ISetupContext context)
                   at Cake.Core.CakeEngine.PerformSetup(ICakeContext context, IExecutionStrategy strategy, CakeTask[] orderedTasks, String target, Stopwatch stopWatch, CakeReport report)
                   at Cake.Core.CakeEngine.RunTargetAsync(ICakeContext context, IExecutionStrategy strategy, ExecutionSettings settings)

                Its because somewhere in your .csproj there is an "Update" package like this:

                  <ItemGroup>
                    <PackageReference Update="Microsoft.SourceLink.GitHub" Version="8.0.0" />
                  </ItemGroup>

                Change it to "Include" or remove 
                 * 
                 * 
                 */

                if (context.FileExists(projectDir.CombineWithFilePath("./Dockerfile")))
                {
                    output = "Docker";
                }
                if (info.IsWebApplication())
                {
                    // Web also means Docker, so it comes after above check
                    output = "Web";
                }
                if (info.IsTestProject())
                {
                    if (!info.PackageReferences.Any(x => x.Name == "coverlet.collector"))
                    {
                        context.Log.Warning($"Assembly {info.AssemblyName} is a test project without \"coverlet.collector\" package. No coverage report possible");
                    }
                    output = "Test";
                }


                binDirs.Add(binDir);

                var filename = project.GetFilename().FullPath;
                projectInfos.Add(new ProjectInfo(filename.Substring(0, filename.Length - 7), info.AssemblyName, output, project, binDir));
            }

            var buildRoot = context.Environment.WorkingDirectory;
            var artifactsDir = buildRoot.Combine("./artifacts");
            var artifactsBinDir = artifactsDir.Combine("bin");
            var testResultsDir = artifactsDir.Combine("test-reports");
            var nugetRoot = artifactsDir.Combine("nuget");

            var zipBinary = artifactsDir.CombineWithFilePath("Build-v" + semVersion + ".zip");

            // Directories
            var buildDirectories = new BuildDirectories(
                buildRoot,
                nugetRoot,
                artifactsDir,
                artifactsBinDir,
                testResultsDir,
                binDirs);
            // Files
            var buildFiles = new BuildFiles(
                projectInfos,
                zipBinary);

            return new BuildPaths
            {
                Files = buildFiles,
                Directories = buildDirectories
            };
        }
    }

    public class BuildDirectories
    {
        public DirectoryPath BuildRoot { get; private set; }
        public DirectoryPath NugetRoot { get; private set; }
        public DirectoryPath ArtifactsDir { get; private set; }
        public DirectoryPath ArtifactsBin { get; private set; }
        public DirectoryPath TestResultsDir { get; private set; }
        public IEnumerable<DirectoryPath> ToClean { get; private set; }

        public BuildDirectories(
            DirectoryPath buildRoot,
            DirectoryPath nugetRoot,
            DirectoryPath artifactsDir,
            DirectoryPath artifactsBinDir,
            DirectoryPath testResultsDir,
            IEnumerable<DirectoryPath> binDirs
            )
        {
            BuildRoot = buildRoot;
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

        public BuildFiles(
            IEnumerable<ProjectInfo> projects,
            FilePath binaries)
        {
            Projects = projects;
            ZipBinaries = binaries;
        }

    }

    public class ProjectInfo
    {
        public string ProjectName { get; private set; }
        public string AssemblyName { get; private set; }
        public string OutputType { get; private set; }
        public FilePath ProjectFile { get; private set; }
        public DirectoryPath OutputPath { get; private set; }

        public ProjectInfo(
            string projectName,
            string assemblyName,
            string outputType,
            FilePath projectFile,
            DirectoryPath outputPath
        )
        {
            ProjectName = projectName;
            AssemblyName = assemblyName;
            OutputType = outputType;
            ProjectFile = projectFile;
            OutputPath = outputPath;
        }
    }
}
