using System.Globalization;
using Microsoft.Diagnostics.Runtime;
using Peerfluence.DumpAnalyzer;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine("Usage: DumpAnalyzer <dump> [--top 1..100] [--root HEX_ADDRESS]...");
    Console.WriteLine("Analyzes locally; prints type names and addresses, never object contents.");
    return args.Length == 0 ? 1 : 0;
}

try
{
    var top = 20;
    var requestedRoots = new List<ulong>();
    for (var i = 1; i < args.Length; i++)
    {
        var option = args[i];
        if (++i >= args.Length)
            throw new ArgumentException($"Missing value for {option}.");
        if (option == "--top" && int.TryParse(args[i], out var count) && count is >= 1 and <= 100)
            top = count;
        else if (option == "--root" && ulong.TryParse(args[i].Replace("0x", "", StringComparison.OrdinalIgnoreCase),
                     NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) && address != 0)
            requestedRoots.Add(address);
        else
            throw new ArgumentException($"Invalid option or value: {option} {args[i]}.");
    }

    using var target = DataTarget.LoadDump(args[0]);
    if (target.ClrVersions.Length != 1)
        throw new InvalidOperationException($"Expected one CLR runtime; found {target.ClrVersions.Length}. Use dotnet-dump for runtime selection.");
    using var runtime = target.ClrVersions[0].CreateRuntime();
    if (!runtime.Heap.CanWalkHeap)
        throw new InvalidOperationException("The GC heap cannot be walked in this dump.");

    Console.WriteLine($"Runtime: {runtime.ClrInfo.Version}; architecture: {target.DataReader.Architecture}; GC: {(runtime.Heap.IsServer ? "server" : "workstation")}");
    var memory = WindowsMemoryMap.Read(args[0]);
    var report = new HeapReport(runtime.Heap);
    report.Write(Console.Out, memory, top, requestedRoots);
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or ClrDiagnosticsException)
{
    Console.Error.WriteLine($"Analysis failed: {ex.Message}");
    return 1;
}
