using System;
using System.Collections.Generic;
using System.Linq;
using Cake.Core;
using Cake.Core.IO;
using Cake.Coverlet;

namespace Build.Tool
{
    static class BuilderExtension
    {
        internal static ProcessArgumentBuilder AppendMSBuildProperty(this ProcessArgumentBuilder builder, string propertyName, string value)
        {
            builder.AppendSwitch($"/property:{propertyName}", "=", value);
            return builder;
        }

        internal static ProcessArgumentBuilder AppendMSBuildPropertyQuoted(this ProcessArgumentBuilder builder, string propertyName, string value)
        {
            builder.AppendSwitchQuoted($"/property:{propertyName}", "=", value);
            return builder;
        }

        internal static ProcessArgumentBuilder AppendPropertyList(this ProcessArgumentBuilder builder, string propertyName, IEnumerable<string> values)
        {
            builder.Append($"/property:{propertyName}=\\\"{string.Join(",", values.Select(s => s.Trim()))}\\\"");
            return builder;
        }

        internal static ProcessArgumentBuilder AppendSwitchQuoted(this ProcessArgumentBuilder builder, string @switch, IEnumerable<string> values)
        {
            foreach (var type in values.Select(s => s.Trim()))
            {
                builder.AppendSwitchQuoted(@switch, type);
            }
            return builder;
        }

        internal static ProcessArgumentBuilder AppendSwitch(this ProcessArgumentBuilder builder, string @switch, IEnumerable<string> values)
        {
            foreach (var type in values.Select(s => s.Trim()))
            {
                builder.AppendSwitch(@switch, type);
            }
            return builder;
        }
        public static void Coverlet(
            this ICakeContext context,
            DirectoryPath testProjectDir,
            CoverletSettings settings,
            bool mine,
            string configuration = "Debug")
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (settings == null)
            {
                settings = new CoverletSettings();
            }

            var projFile = context.Globber.GetFiles($"{testProjectDir}/*.*proj")
                .FirstOrDefault();

            if (projFile == null)
            {
                throw new Exception($"Could not find valid proj file in {testProjectDir}");
            }

            var debugFile = FindDebugDll(context, testProjectDir, projFile, configuration);

            new Tool.CoverletTool(context.FileSystem, context.Environment, context.ProcessRunner, context.Tools)
                .Run(debugFile, projFile, settings, configuration);
        }

        private static FilePath FindDebugDll(ICakeContext context, DirectoryPath path, FilePath filename, string configuration)
        {
            var nameWithoutExtension = filename.GetFilenameWithoutExtension();
            var debugFile = context.Globber.GetFiles($"{path.MakeAbsolute(context.Environment)}/bin/**/{configuration}/**/{nameWithoutExtension}.dll")
                .FirstOrDefault();

            if (debugFile == null)
            {
                throw new Exception($"Could not find {configuration} dll with name {nameWithoutExtension}.dll");
            }

            return debugFile;
        }
    }
}
