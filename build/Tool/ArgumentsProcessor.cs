using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cake.Core;
using Cake.Core.IO;
using Cake.Coverlet;

namespace Build.Tool
{

    static class ArgumentsProcessor
    {
        

        public static ProcessArgumentBuilder ProcessToolArguments(
                CoverletSettings settings,
                ICakeEnvironment cakeEnvironment,
                ProcessArgumentBuilder builder,
                FilePath project)
        {
            builder.AppendSwitch("--format", SplitFlagEnum(settings.CoverletOutputFormat));

            if (settings.Threshold.HasValue)
            {
                if (settings.Threshold > 100)
                {
                    throw new Exception("Threshold Percentage cannot be set as greater than 100%");
                }

                builder.AppendSwitch(nameof(CoverletSettings.Threshold), settings.Threshold.ToString());

                if (settings.ThresholdType != ThresholdType.NotSet)
                {
                    builder.AppendSwitchQuoted("--threshold-type", SplitFlagEnum(settings.ThresholdType));
                }
            }

            if (settings.CoverletOutputDirectory != null && string.IsNullOrEmpty(settings.CoverletOutputName))
            {
                var directoryPath = settings.CoverletOutputDirectory
                    .MakeAbsolute(cakeEnvironment).FullPath;

                builder.AppendSwitchQuoted("--output", directoryPath);
            }
            else if (!string.IsNullOrEmpty(settings.CoverletOutputName))
            {
                var dir = settings.CoverletOutputDirectory ?? project.GetDirectory();
                var directoryPath = dir.MakeAbsolute(cakeEnvironment).FullPath;

                var filepath = FilePath.FromString(settings.OutputTransformer(settings.CoverletOutputName, directoryPath));

                builder.AppendSwitchQuoted("--output", filepath.MakeAbsolute(cakeEnvironment).FullPath);
            }

            if (settings.ExcludeByFile.Count > 0)
            {
                builder.AppendSwitchQuoted("--exclude-by-file", settings.ExcludeByFile);
            }

            if (settings.ExcludeByAttribute.Count > 0)
            {
                builder.AppendSwitchQuoted("--exclude-by-attribute", settings.ExcludeByAttribute);
            }

            if (settings.Exclude.Count > 0)
            {
                builder.AppendSwitchQuoted("--exclude", settings.Exclude);
            }

            if (settings.Include.Count > 0)
            {
                builder.AppendSwitchQuoted("--include", settings.Include);
            }

            if (settings.MergeWithFile != null && settings.MergeWithFile.GetExtension() == ".json")
            {
                builder.AppendSwitchQuoted("--merge-with", settings.MergeWithFile.MakeAbsolute(cakeEnvironment).FullPath);
            }

            if (settings.IncludeTestAssembly.HasValue)
            {
                builder.AppendSwitch("--include-test-assembly", settings.IncludeTestAssembly.Value ? bool.TrueString : bool.FalseString);
            }

            return builder;
        }

        private static IEnumerable<string> SplitFlagEnum(Enum @enum) => @enum.ToString("g").Split(',').Select(s => s.ToLowerInvariant());
    }
}
