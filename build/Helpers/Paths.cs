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

                if (context.FileExists(projectDir.CombineWithFilePath("./Dockerfile")))
                {
                    output = "Docker";
                }
                if (info.IsWebApplication())
                {
                    // Web also means Docker, so it comes after above check
                    output = "Web";
                }
                if (info.IsDotNetCliTestProject())
                {
                    output = "Test";
                }
                // dont build "build" projects
                if (info.HasPackage("Cake.Core") || info.HasPackage("Cake.Frosting"))
                    continue;
                

                binDirs.Add(binDir);

                var filename = project.GetFilename().FullPath;
                projectInfos.Add(new ProjectInfo(filename.Substring(0, filename.Length - 7), info.AssemblyName, output, project, binDir));
            }

            var buildRoot = (DirectoryPath)context.Directory(".");
            var artifactsDir = (DirectoryPath)context.Directory("./artifacts");
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
