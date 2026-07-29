using System.Reflection;
using System.Runtime.Loader;

namespace EricoreTech.CandleSharp.Infrastructure
{
    /// <summary>
    /// Loads plugin implementations of a Core contract (ITechnicalIndicator,
    /// ITradingAgent) from DLLs. A plugin is any .NET assembly referencing
    /// EricoreTech.CandleSharp.Core that contains public, concrete implementations with a
    /// parameterless or all-defaults constructor. Drop the DLL into the plugins
    /// directory and it is picked up at startup.
    /// </summary>
    internal static class PluginLoader
    {
        public static List<(TContract Instance, string Source)> LoadFrom<TContract>(
            string directory, Action<string>? onError = null) where TContract : class
        {
            var found = new List<(TContract, string)>();
            if (!Directory.Exists(directory)) return found;

            string coreName = typeof(TContract).Assembly.GetName().Name!;
            foreach (var dll in Directory.GetFiles(directory, "*.dll").Order())
            {
                Assembly assembly;
                try
                {
                    // A copy of Core itself (e.g. published alongside a plugin) is the
                    // host's contract assembly, never a plugin — don't re-register its
                    // built-ins.
                    if (AssemblyName.GetAssemblyName(dll).Name == coreName)
                        continue;
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll));
                }
                catch (Exception ex) when (ex is BadImageFormatException or FileLoadException)
                {
                    onError?.Invoke($"{dll}: not a loadable plugin ({ex.Message})");
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t is not null).ToArray()!;
                }

                foreach (var type in types.OrderBy(t => t?.FullName, StringComparer.Ordinal))
                {
                    // Types compiled against a mismatched copy of Core simply fail
                    // the IsAssignableFrom check and are skipped.
                    if (!typeof(TContract).IsAssignableFrom(type)
                        || type.IsAbstract || type.IsInterface || !type.IsPublic)
                        continue;

                    try
                    {
                        if (Instantiate(type) is TContract instance)
                            found.Add((instance, $"plugin:{Path.GetFileName(dll)}"));
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke($"{dll}: failed to create {type.Name} ({ex.Message})");
                    }
                }
            }
            return found;
        }

        private static object? Instantiate(Type type)
        {
            // Prefer a parameterless constructor; otherwise use one where every
            // parameter has a default (the primary-constructor-with-defaults style).
            var ctors = type.GetConstructors();
            var parameterless = ctors.FirstOrDefault(c => c.GetParameters().Length == 0);
            if (parameterless is not null) return parameterless.Invoke([]);

            var withDefaults = ctors.FirstOrDefault(c => c.GetParameters().All(p => p.HasDefaultValue));
            return withDefaults?.Invoke(withDefaults.GetParameters().Select(p => p.DefaultValue).ToArray());
        }
    }
}
