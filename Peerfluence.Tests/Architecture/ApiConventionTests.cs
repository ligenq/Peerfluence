using System.Reflection;
using System.Runtime.CompilerServices;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Architecture;

/// <summary>
/// API conventions whose violations compile successfully but make cancellation harder to forward
/// and call sites harder to evolve.
/// </summary>
public sealed class ApiConventionTests
{
    [Fact]
    public void EveryCancellationToken_IsTheLastParameter()
    {
        // Optional arguments after a token force callers to choose between positional arguments in
        // the wrong conventional order and named arguments for ordinary data. More importantly,
        // APIs that forward cancellation compose predictably only when the token is always last.
        // CA1068 covers public APIs; this guard also covers the internal helpers where most of the
        // application's orchestration lives.
        var offenders = ProductionAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .SelectMany(DeclaredMethodsAndConstructors)
            .Select(member => (Member: member, Parameters: member.GetParameters()))
            .Where(item => item.Parameters.Any(parameter => parameter.ParameterType == typeof(CancellationToken)))
            .Where(item => item.Parameters[^1].ParameterType != typeof(CancellationToken))
            .Select(item => $"  {item.Member.DeclaringType?.FullName}.{item.Member.Name}({string.Join(", ", item.Parameters.Select(parameter => parameter.ParameterType.Name + " " + parameter.Name))})")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} members put an argument after their CancellationToken:{Environment.NewLine}"
                + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<Assembly> ProductionAssemblies()
    {
        yield return typeof(App).Assembly;
        yield return typeof(ITorrentService).Assembly;
    }

    private static IEnumerable<MethodBase> DeclaredMethodsAndConstructors(Type type)
    {
        const BindingFlags flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        return type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags));
    }
}
