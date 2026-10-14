using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace DotNative.Development;

internal static class PreviewApplication
{
    // This entry is used only by the managed preview. NativeAOT uses the generated
    // direct call, which keeps the application's entry point visible to the linker.
    [RequiresUnreferencedCode("Managed preview loads the application's compiled entry point.")]
    [RequiresDynamicCode("Managed preview is not a NativeAOT application.")]
    internal static Task Invoke(string assemblyName, string startupType)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));
        var type = assembly.GetType(startupType, throwOnError: true)!;
        var candidates = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            )
            .Where(method =>
                method.Name == "Main"
                && !method.IsGenericMethod
                && (
                    method.ReturnType == typeof(void)
                    || method.ReturnType == typeof(int)
                    || method.ReturnType == typeof(Task)
                    || method.ReturnType == typeof(Task<int>)
                )
            )
            .Where(method =>
                method.GetParameters().Length == 0
                || method.GetParameters() is [{ ParameterType: var parameter }]
                    && parameter == typeof(string[])
            )
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();
        if (candidates.Length == 0)
            throw new MissingMethodException(startupType, "Main() or Main(string[])");
        var main = candidates[0];
        object? result;
        try
        {
            result = main.Invoke(
                null,
                main.GetParameters().Length == 0 ? null : [Array.Empty<string>()]
            );
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        if (typeof(Task).IsAssignableFrom(main.ReturnType))
            return result as Task
                ?? throw new InvalidOperationException("Main returned a null Task.");
        return Task.CompletedTask;
    }
}
