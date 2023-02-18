using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace dns_sync.plugins
{
    public class PluginLibrary
    {
        private Dictionary<string, Type> Plugins { get; }
        private HashSet<Type> AlreadyAdded { get; }

        public PluginLibrary()
        {
            AlreadyAdded = new HashSet<Type>();
            Plugins = new Dictionary<string, Type>();
        }

        public PluginLibrary AddPluginsFromAssembly<T>()
        {
            var assembly = typeof(T).GetTypeInfo().Assembly;

            foreach (var type in assembly.ExportedTypes)
            {
                if (AlreadyAdded.Contains(type))
                {
                    continue;
                }

                var typeInfo = type.GetTypeInfo();

                if (!typeof(IDnsSyncPlugin).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
                {
                    continue;
                }

                if (!typeInfo.IsClass)
                {
                    continue;
                }

                if (typeInfo.IsAbstract)
                {
                    continue;
                }


                var detailedInfo = GetTypeInstance(type);

                if (Plugins.ContainsKey(detailedInfo.Key))
                {
                    throw new Exception($"Type '{detailedInfo.Value.FullName}' is trying to register a duplicate plugin name: {detailedInfo.Key}");
                }

                Plugins.Add(detailedInfo.Key, detailedInfo.Value);
                AlreadyAdded.Add(detailedInfo.Value);
            }

            return this;
        }

        public IDnsSyncPlugin GetPlugin(string pluginName)
        {
            if (!Plugins.ContainsKey(pluginName))
            {
                throw new Exception($"There is no plugin registered with the name {pluginName}");
            }

            var type = Plugins[pluginName];
            var typeInfo = type.GetTypeInfo();

            var constructors = typeInfo.DeclaredConstructors;
            var constructorInformation = constructors.FirstOrDefault(c => c.IsPublic && c.GetParameters().Length == 0);

            if (constructorInformation == null)
            {
                throw new ArgumentException($"Type '{type.FullName}' does not have a public parameterless constructor", nameof(type));
            }

            return (IDnsSyncPlugin)constructorInformation.Invoke(Array.Empty<object>());
        }

        private static KeyValuePair<string, Type> GetTypeInstance(Type type)
        {
            var typeInfo = type.GetTypeInfo();

            if (!typeof(IDnsSyncPlugin).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
            {
                throw new ArgumentException($"Type '{type.FullName}' does not implement '{typeof(IDnsSyncPlugin).FullName}'", nameof(type));
            }

            if (!typeInfo.IsClass)
            {
                throw new ArgumentException($"Type '{type.FullName}' is not a class and cannot be instantiated", nameof(type));
            }

            if (typeInfo.IsAbstract)
            {
                throw new ArgumentException($"Type '{type.FullName}' is abstract and cannot be instantiated", nameof(type));
            }

            var wwww = typeInfo.GetProperties();

            var nameProperty = wwww.FirstOrDefault(c => c.Name == nameof(IDnsSyncPlugin.PluginName) && c.GetAccessors(nonPublic: true)[0].IsStatic);

            if (nameProperty == null)
            {
                throw new ArgumentException($"Type '{type.FullName}' does not implement the {nameof(IDnsSyncPlugin.PluginName)} property", nameof(type));
            }

            var pluginName = nameProperty.GetValue(null) as string;

            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentException($"Type '{type.FullName}' has an invalid {nameof(IDnsSyncPlugin.PluginName)}", nameof(type));
            }

            return new KeyValuePair<string, Type>(pluginName, typeInfo);
        }
    }
}
