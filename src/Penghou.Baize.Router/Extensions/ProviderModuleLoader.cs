using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Penghou.Baize.Router.Configuration;
using System.Reflection;
using System.Runtime.Loader;

namespace Penghou.Baize.Router.Extensions;

internal static class ProviderModuleLoader
{
    public static void Register(
        IServiceCollection services,
        IEnumerable<LlmProviderModuleOptions> modules)
    {
        foreach (var module in modules)
        {
            var assembly = LoadAssembly(module);
            var providerTypes = ResolveProviderTypes(assembly, module).ToList();

            if (providerTypes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Provider assembly '{module.Assembly}' contains no public, " +
                    $"concrete {nameof(ILlmClientProvider)} implementation.");
            }

            foreach (var providerType in providerTypes)
            {
                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton(typeof(ILlmClientProvider), providerType));
            }
        }
    }

    private static Assembly LoadAssembly(LlmProviderModuleOptions module)
    {
        if (string.IsNullOrWhiteSpace(module.Assembly))
            throw new InvalidOperationException("A provider module has no assembly name.");

        if (module.Assembly.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidOperationException(
                $"Provider module '{module.Assembly}' must be an assembly name, not a path.");
        }

        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(
                new AssemblyName(module.Assembly));
        }
        catch (Exception exception) when (
            exception is ArgumentException or FileNotFoundException or
                FileLoadException or BadImageFormatException)
        {
            throw new InvalidOperationException(
                $"Could not load LLM provider assembly '{module.Assembly}'. " +
                "Ensure its NuGet package is referenced by the application.",
                exception);
        }
    }

    private static IEnumerable<Type> ResolveProviderTypes(
        Assembly assembly,
        LlmProviderModuleOptions module)
    {
        if (!string.IsNullOrWhiteSpace(module.Type))
        {
            var configuredType = assembly.GetType(
                module.Type,
                throwOnError: false,
                ignoreCase: false);

            if (configuredType is null)
            {
                throw new InvalidOperationException(
                    $"Provider type '{module.Type}' was not found in assembly " +
                    $"'{assembly.GetName().Name}'.");
            }

            EnsureProviderType(configuredType);
            return [configuredType];
        }

        try
        {
            return assembly.ExportedTypes.Where(IsProviderType).ToArray();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var details = string.Join(
                "; ",
                exception.LoaderExceptions
                    .Where(loader => loader is not null)
                    .Select(loader => loader!.Message));
            throw new InvalidOperationException(
                $"Could not inspect LLM provider assembly " +
                $"'{assembly.GetName().Name}': {details}",
                exception);
        }
    }

    private static void EnsureProviderType(Type type)
    {
        if (!IsProviderType(type))
        {
            throw new InvalidOperationException(
                $"Configured provider type '{type.FullName}' must be public, " +
                $"concrete, and implement {nameof(ILlmClientProvider)}.");
        }
    }

    private static bool IsProviderType(Type type) =>
        type is { IsVisible: true, IsAbstract: false, IsInterface: false } &&
        typeof(ILlmClientProvider).IsAssignableFrom(type);
}
