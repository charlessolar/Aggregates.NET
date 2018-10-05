using Aggregates.Extensions;
using Aggregates.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Aggregates.Internal
{
    public class VersionRegistrar : Contracts.IVersionRegistrar
    {
        private static readonly Regex NameRegex = new Regex(@"^(?<Namespace>\S+)\.(?<Name>\S+)\sv(?<Version>[0-9]+)$", RegexOptions.Compiled);
        private static readonly ILog Logger = LogProvider.GetLogger("VersionRegistrar");

        public class VersionDefinition
        {
            public int Version { get; private set; }
            public string Name { get; private set; }
            public string Namespace { get; private set; }
            public Type Type { get; private set; }
            public VersionDefinition(string name, string @namespace, int version, Type type)
            {
                this.Version = version;
                this.Name = name;
                this.Namespace = @namespace;
                this.Type = type;
            }
        }


        private static object _sync = new object();
        private static Dictionary<string, List<VersionDefinition>> NameToType = new Dictionary<string, List<VersionDefinition>>();
        private static Dictionary<Type, VersionDefinition> TypeToDefinition = new Dictionary<Type, VersionDefinition>();

        private Contracts.IMessaging _messaging;

        public VersionRegistrar(Contracts.IMessaging messaging)
        {
            _messaging = messaging;

            Load(_messaging.GetMessageTypes());
            Load(_messaging.GetEntityTypes());
        }


        public void Load(Type[] types)
        {
            lock (_sync)
            {
                foreach (var type in types)
                {
                    var versionInfo = type.GetCustomAttributes().OfType<Versioned>().SingleOrDefault();
                    if (versionInfo == null)
                    {
                        Logger.WarnEvent("ShouldVersion", "{TypeName} needs a [Versioned] attribute", type.FullName);
                        versionInfo = new Versioned(type.Name, type.Assembly.GetName().Name, 1);
                    }
                    Logger.DebugEvent("VersionedType", "{Namespace}.{TypeName} version {Version} found", versionInfo.Namespace, versionInfo.Name, versionInfo.Version);
                    RegisterType(type, versionInfo.Name, versionInfo.Namespace, versionInfo.Version);
                }
            }
        }

        private void RegisterType(Type type, string name, string @namespace, int version)
        {
            if (!NameToType.TryGetValue($"{@namespace}.{name}", out var list))
                list = new List<VersionDefinition>();

            var definition = new VersionDefinition(name, @namespace, version, type);

            TypeToDefinition[type] = definition;
            if (list.Any(x => x.Name == name && x.Namespace == @namespace && x.Version == version))
                return;

            list.Add(definition);
            NameToType[$"{@namespace}.{name}"] = list;
        }

        public string GetVersionedName(Type versionedType)
        {
            var contains = false;

            lock (_sync)
            {
                contains = TypeToDefinition.ContainsKey(versionedType);
            }
            if (!contains)
                Load(new[] { versionedType });

            lock (_sync)
            {
                var definition = TypeToDefinition[versionedType];
                return $"{definition.Namespace}.{definition.Name} v{definition.Version}";
            }
        }
        public Type GetNamedType(string versionedName)
        {
            var match = NameRegex.Match(versionedName);
            if (!match.Success)
                throw new ArgumentException($"{versionedName} is not the right format");

            var @namespace = match.Groups["Namespace"].Value;
            var name = match.Groups["Name"].Value;
            var version = match.Groups["Version"].Value;

            lock (_sync)
            {
                if (!NameToType.TryGetValue($"{@namespace}.{name}", out var definitions) || !definitions.Any())
                {
                    Logger.WarnEvent("TypeMissing", "{TypeName} is not registered", versionedName);
                    throw new ArgumentException($"{versionedName} is not registered");
                }
                if (!int.TryParse(version, out var intVersion))
                    return definitions.OrderByDescending(x => x.Version).First().Type;

                var type = definitions.SingleOrDefault(x => x.Version == intVersion)?.Type;
                if(type == null)
                {
                    Logger.WarnEvent("VersionMissing", "Missing version {Version} for {TypeName}", intVersion, versionedName);
                    throw new ArgumentException($"Missing version {intVersion} for {versionedName}");
                }

                return type;
            }
        }
    }
}
