using System.Reflection;

namespace Peerfluence.Tests.Architecture;

public class EntryPointTests
{
    /// <summary>
    /// The entry point has to stay synchronous, and it is not obvious why from reading it.
    ///
    /// <para>
    /// <c>[STAThread]</c> applies to the thread the runtime creates. An <c>async Task Main</c> holds
    /// that thread only as far as its first await; everything after resumes on a thread-pool thread,
    /// which belongs to no apartment. Since the Avalonia UI loop is started after that point, the UI
    /// thread itself ends up outside the apartment, and the OLE clipboard refuses every call from
    /// there with "CoInitialize has not been called" - which is how the copy commands came to fail
    /// silently.
    /// </para>
    /// </summary>
    [Fact]
    public void Main_IsSynchronous_SoTheUiKeepsTheApartmentItWasGiven()
    {
        var main = EntryPoint();

        Assert.Equal(typeof(void), main.ReturnType);
    }

    [Fact]
    public void Main_RunsInASingleThreadedApartment()
    {
        var main = EntryPoint();

        Assert.True(
            main.IsDefined(typeof(STAThreadAttribute), inherit: false),
            "The entry point must be [STAThread]: the Windows clipboard, drag and drop and the shell dialogs are all OLE.");
    }

    private static MethodInfo EntryPoint()
    {
        var program = typeof(Peerfluence.App).Assembly.GetType("Peerfluence.Program");
        Assert.NotNull(program);

        var main = program.GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(main);
        return main;
    }
}
