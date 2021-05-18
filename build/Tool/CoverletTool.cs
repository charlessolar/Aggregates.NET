using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cake.Core;
using Cake.Core.IO;
using Cake.Core.Tooling;
using Cake.Coverlet;

// workaround until https://github.com/Romanx/Cake.Coverlet/issues/44 is fixed
namespace Build.Tool
{/// <summary>
 /// A class for the coverlet tool
 /// </summary>
    public sealed class CoverletTool : Tool<CoverletSettings>
    {
        private readonly ICakeEnvironment _environment;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Cake.Common.Tools.DotNetCore.VSTest.DotNetCoreVSTester" /> class.
        /// </summary>
        /// <param name="fileSystem">The file system.</param>
        /// <param name="environment">The environment.</param>
        /// <param name="processRunner">The process runner.</param>
        /// <param name="tools">The tool locator.</param>
        public CoverletTool(IFileSystem fileSystem, ICakeEnvironment environment, IProcessRunner processRunner, IToolLocator tools)
          : base(fileSystem, environment, processRunner, tools)
        {
            _environment = environment;
        }

        /// <summary>
        /// Gets the name of the tool.
        /// </summary>
        /// <returns>The name of the tool</returns>
        protected override string GetToolName() => "coverlet";

        /// <summary>
        /// Runs the tool with the given test file, test project and settings
        /// </summary>
        /// <param name="testFile"></param>
        /// <param name="testProject"></param>
        /// <param name="settings"></param>
        public void Run(FilePath testFile, FilePath testProject, CoverletSettings settings, string configuration)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Run(settings, GetArguments(testFile, testProject, settings, configuration));
        }

        private ProcessArgumentBuilder GetArguments(
            FilePath coverageFile,
            FilePath testProject,
            CoverletSettings settings,
            string configuration)
        {
            var argumentBuilder = new ProcessArgumentBuilder();

            argumentBuilder.AppendQuoted(coverageFile.MakeAbsolute(_environment).FullPath);

            argumentBuilder.AppendSwitchQuoted("--target", "dotnet");
            argumentBuilder.AppendSwitchQuoted($"--targetargs", $"test {testProject.MakeAbsolute(_environment)} --no-build --configuration {configuration}");

            ArgumentsProcessor.ProcessToolArguments(settings, _environment, argumentBuilder, testProject);

            return argumentBuilder;
        }

        /// <summary>
        /// Gets the possible executable names
        /// </summary>
        /// <returns></returns>
        protected override IEnumerable<string> GetToolExecutableNames() => new[] { "coverlet", "coverlet.exe" };

        /// <summary>
        /// Gets the alternative tool paths for the tool
        /// </summary>
        /// <param name="settings"></param>
        /// <returns>The alternate tool paths</returns>
        protected override IEnumerable<FilePath> GetAlternativeToolPaths(CoverletSettings settings)
        {
            var globalToolLocation = _environment.Platform.IsUnix()
                ? new DirectoryPath(_environment.GetEnvironmentVariable("HOME"))
                : new DirectoryPath(_environment.GetEnvironmentVariable("USERPROFILE"));

            return GetToolExecutableNames()
                .Select(name => globalToolLocation.Combine(".dotnet/tools").GetFilePath(name));
        }
    }
}
