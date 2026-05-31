using Microsoft.Diagnostics.Runtime;

if (args.Length == 0) { Console.WriteLine("Usage: DumpAnalyzer <dump>"); return; }

Console.WriteLine($"Opening: {args[0]}");
using var target = DataTarget.LoadDump(args[0]);
var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;

long totalHeap = heap.Segments.Sum(s => (long)s.Length);
Console.WriteLine($"\nHeap: {totalHeap / 1024.0 / 1024.0:F1} MB, Segments: {heap.Segments.Length}");

// === GC segments ===
Console.WriteLine("\n=== GC SEGMENTS ===");
foreach (var g in heap.Segments.GroupBy(s => s.Kind))
    Console.WriteLine($"  {g.Key}: {g.Sum(s => (long)s.Length) / 1024.0 / 1024.0:F1} MB ({g.Count()} segs)");

// === Type stats ===
var typeStats = new Dictionary<string, (long Count, long Size)>();
foreach (var obj in heap.EnumerateObjects())
{
    if (obj.Type == null) continue;
    string name = obj.Type.Name ?? "?";
    long size = (long)obj.Size;
    if (typeStats.TryGetValue(name, out var e))
        typeStats[name] = (e.Count + 1, e.Size + size);
    else
        typeStats[name] = (1, size);
}

Console.WriteLine("\n=== TOP 20 TYPES BY SIZE ===");
Console.WriteLine($"{"Count",12} {"TotalMB",10} {"AvgB",10}  Type");
foreach (var kv in typeStats.OrderByDescending(x => x.Value.Size).Take(20))
{
    var (c, s) = kv.Value;
    Console.WriteLine($"{c,12:N0} {s / 1024.0 / 1024.0,10:F1} {(c > 0 ? s / c : 0),10:N0}  {kv.Key}");
}

// === Byte[] details ===
Console.WriteLine("\n=== BYTE[] SIZE DISTRIBUTION ===");
long totalBA = 0; long totalBASize = 0;
var buckets = new SortedDictionary<string, (long Count, long Size)>();
foreach (var obj in heap.EnumerateObjects())
{
    if (obj.Type?.Name != "System.Byte[]") continue;
    totalBA++; var sz = (long)obj.Size; totalBASize += sz;
    string b = sz switch { <= 256 => "a:0-256B", <= 1024 => "b:257B-1KB", <= 4096 => "c:1-4KB",
        <= 16384 => "d:4-16KB", <= 32768 => "e:16-32KB", <= 65536 => "f:32-64KB",
        <= 85000 => "g:64-85KB", <= 262144 => "h:85-256KB(LOH)", <= 1048576 => "i:256KB-1MB(LOH)",
        _ => "j:>1MB(LOH)" };
    if (buckets.TryGetValue(b, out var g)) buckets[b] = (g.Count + 1, g.Size + sz);
    else buckets[b] = (1, sz);
}
Console.WriteLine($"Total: {totalBA:N0} arrays, {totalBASize / 1024.0 / 1024.0:F1} MB");
foreach (var kv in buckets)
    Console.WriteLine($"  {kv.Key.Substring(2),-20} {kv.Value.Count,10:N0} {kv.Value.Size / 1024.0 / 1024.0,10:F1} MB");

// === All byte[] > 85KB (LOH) - exact sizes ===
Console.WriteLine("\n=== ALL LOH BYTE[] (>85KB) - exact sizes ===");
var lohArrays = new List<(ulong Addr, long Size)>();
foreach (var obj in heap.EnumerateObjects())
{
    if (obj.Type?.Name == "System.Byte[]" && obj.Size > 85000)
        lohArrays.Add((obj.Address, (long)obj.Size));
}
lohArrays.Sort((a, b) => b.Size.CompareTo(a.Size));
foreach (var (addr, sz) in lohArrays)
    Console.WriteLine($"  {addr:X16}  {sz / 1024.0 / 1024.0,8:F2} MB  ({sz:N0} bytes)");

// === Who references LOH byte[] ===
Console.WriteLine("\n=== REFERENCE CHAIN TO LOH BYTE[] ===");
var lohAddrs = new HashSet<ulong>(lohArrays.Select(x => x.Addr));
// Direct references
var directRefs = new Dictionary<ulong, List<(string TypeName, ulong ObjAddr)>>();
foreach (var addr in lohAddrs)
    directRefs[addr] = new();

Console.Error.Write("Scanning references");
long scanned = 0;
foreach (var obj in heap.EnumerateObjects())
{
    scanned++;
    if (scanned % 100000 == 0) Console.Error.Write(".");
    if (obj.Type == null) continue;
    foreach (var r in obj.EnumerateReferences())
    {
        if (lohAddrs.Contains(r.Address))
            directRefs[r.Address].Add((obj.Type.Name ?? "?", obj.Address));
    }
}
Console.Error.WriteLine(" done");

foreach (var (addr, sz) in lohArrays)
{
    var refs = directRefs[addr];
    if (refs.Count == 0)
        Console.WriteLine($"  {addr:X16} ({sz / 1024.0 / 1024.0:F1} MB): UNREFERENCED (dead)");
    else
    {
        foreach (var (tn, oa) in refs.Take(3))
            Console.WriteLine($"  {addr:X16} ({sz / 1024.0 / 1024.0:F1} MB): <- {tn} @ {oa:X}");
    }
}

// === GC roots for LOH byte[] ===
Console.WriteLine("\n=== GC HANDLE ROOTS FOR LOH BYTE[] ===");
foreach (var handle in runtime.EnumerateHandles())
{
    if (lohAddrs.Contains(handle.Object))
        Console.WriteLine($"  {handle.Object:X16} rooted by {handle.HandleKind}");
}

Console.WriteLine("\n=== GC HEAP ROOTS FOR LOH BYTE[] ===");
foreach (var root in heap.EnumerateRoots())
{
    if (lohAddrs.Contains(root.Object.Address))
        Console.WriteLine($"  {root.Object.Address:X16} rooted by {root.RootKind} @ {root.Address:X}");
}

// === Free space analysis ===
Console.WriteLine("\n=== FREE SPACE ON LOH ===");
long freeOnLoh = 0;
long freeCount = 0;
foreach (var obj in heap.EnumerateObjects())
{
    if (obj.Type?.Name == "Free" && obj.Size > 85000)
    {
        freeOnLoh += (long)obj.Size;
        freeCount++;
    }
}
Console.WriteLine($"  {freeCount:N0} free regions, {freeOnLoh / 1024.0 / 1024.0:F1} MB total");
