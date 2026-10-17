using Microsoft.Diagnostics.Runtime;

namespace Peerfluence.DumpAnalyzer;

internal sealed class HeapReport
{
    private readonly ClrHeap _heap;
    private readonly Dictionary<ulong, ulong> _parents = [];
    private readonly Dictionary<ulong, string> _roots = [];

    internal HeapReport(ClrHeap heap) => _heap = heap;

    internal void Write(TextWriter writer, WindowsMemoryMap? memory, int top, List<ulong> requestedRoots)
    {
        Console.Error.WriteLine("Tracing strong GC roots (including dependent handles and frozen objects)...");
        TraceRoots();
        var types = new Dictionary<string, TypeStat>(StringComparer.Ordinal);
        var segments = new Dictionary<GCSegmentKind, SegmentStat>();
        var byteSizes = new Dictionary<ulong, TypeStat>();
        var byteParents = new Dictionary<string, TypeStat>(StringComparer.Ordinal);
        var largest = new PriorityQueue<ClrObject, ulong>();
        ulong unreachable = 0, reachable = 0, free = 0;

        // One physical heap walk. Free blocks are classified by segment, never by size.
        foreach (var segment in _heap.Segments)
        {
            var stat = segments.GetValueOrDefault(segment.Kind) ?? new SegmentStat();
            segments[segment.Kind] = stat;
            stat.Count++;
            stat.Occupied += segment.Length;
            stat.Committed += segment.CommittedMemory.Length;
            foreach (var obj in segment.EnumerateObjects(carefully: true))
            {
                if (obj.IsFree)
                {
                    free += obj.Size;
                    stat.Free += obj.Size;
                    continue;
                }
                if (!obj.IsValid)
                    throw new InvalidOperationException($"Unreadable object at {obj.Address:x}; totals would be incomplete.");
                var isReachable = _parents.ContainsKey(obj.Address);
                if (isReachable) reachable += obj.Size;
                else unreachable += obj.Size;
                Add(types, obj.Type?.Name ?? "<unknown>", obj.Size, isReachable);
                largest.Enqueue(obj, obj.Size);
                if (largest.Count > top) largest.Dequeue();
                if (obj.Type?.Name != "System.Byte[]") continue;
                Add(byteSizes, obj.Size, obj.Size, isReachable);
                if (isReachable)
                {
                    var parent = _parents[obj.Address];
                    var parentName = parent == 0 ? "<direct GC/frozen root>" : _heap.GetObject(parent).Type?.Name ?? "<unknown>";
                    Add(byteParents, parentName, obj.Size, true);
                }
            }
        }

        writer.WriteLine("\n=== MEMORY ACCOUNTING (MiB = 1,048,576 bytes) ===");
        memory?.Write(writer, _heap.Segments.Select(s => (s.CommittedMemory.Start, s.CommittedMemory.End)).ToArray(), top);
        if (memory == null) writer.WriteLine("Windows virtual-memory map unavailable in this dump.");
        writer.WriteLine($"Active segment commitment (including frozen): {Mib(segments.Values.Aggregate(0UL, (sum, s) => sum + s.Committed)):F2} MiB");
        writer.WriteLine($"Strong-root reachable objects:                {Mib(reachable):F2} MiB");
        writer.WriteLine($"Objects not reached from those roots:         {Mib(unreachable):F2} MiB");
        writer.WriteLine($"GC free blocks within active segments:        {Mib(free):F2} MiB");
        writer.WriteLine("Unreached objects are potential garbage, not proven leaks. Pending finalizers count as roots.");
        writer.WriteLine("Reachable size is shallow object size, not exclusive/dominator retained size.");
        writer.WriteLine("Commitment, dump file size, and Task Manager working set are different measurements.");

        writer.WriteLine("\n=== ACTIVE SEGMENTS ===");
        writer.WriteLine($"{"Kind",-16} {"Count",7} {"Occupied MiB",14} {"Committed MiB",14} {"Free MiB",12}");
        foreach (var (kind, stat) in segments.OrderBy(kv => kv.Key))
            writer.WriteLine($"{kind,-16} {stat.Count,7} {Mib(stat.Occupied),14:F2} {Mib(stat.Committed),14:F2} {Mib(stat.Free),12:F2}");
        WriteTypes(writer, "TYPES BY PHYSICAL OBJECT SIZE", types.OrderByDescending(kv => kv.Value.Size).Take(top));
        WriteTypes(writer, "TYPES BY STRONG-ROOT REACHABLE SIZE", types.OrderByDescending(kv => kv.Value.ReachableSize).Take(top));
        WriteTypes(writer, "BYTE[] EXACT SIZES (including object headers)", byteSizes.OrderByDescending(kv => kv.Value.Size).Take(top).Select(kv => new KeyValuePair<string, TypeStat>($"{kv.Key:N0} bytes", kv.Value)));
        WriteTypes(writer, "REACHABLE BYTE[] BY FIRST PARENT ON A ROOT PATH", byteParents.OrderByDescending(kv => kv.Value.Size).Take(top));
        writer.WriteLine("Parent groups show one discovered path per array; an array can have other owners.");

        writer.WriteLine($"\n=== LARGEST {top} OBJECTS AND ROOT PATHS ===");
        var objects = largest.UnorderedItems.Select(item => item.Element).OrderByDescending(o => o.Size);
        foreach (var obj in objects) WritePath(writer, obj.Address);
        foreach (var address in requestedRoots.Distinct()) WritePath(writer, address);
    }

    private void TraceRoots()
    {
        var pending = new Queue<ulong>();
        foreach (var root in _heap.EnumerateRoots())
        {
            var address = root.Object.Address;
            if (address == 0 || !_parents.TryAdd(address, 0)) continue;
            _roots[address] = $"{root.RootKind} @ {root.Address:x}";
            pending.Enqueue(address);
        }
        // Frozen objects are never collected, even when no ordinary root points at them.
        foreach (var segment in _heap.Segments.Where(s => s.Kind == GCSegmentKind.Frozen))
        foreach (var obj in segment.EnumerateObjects(carefully: true))
        {
            if (obj.IsFree || !_parents.TryAdd(obj.Address, 0)) continue;
            _roots[obj.Address] = "Frozen segment";
            pending.Enqueue(obj.Address);
        }
        while (pending.TryDequeue(out var address))
        {
            var obj = _heap.GetObject(address);
            if (!obj.IsValid || obj.IsFree)
                throw new InvalidOperationException($"Unreadable rooted object at {address:x}; reachability would be incomplete.");
            foreach (var reference in obj.EnumerateReferences(considerDependantHandles: true, carefully: true))
            {
                if (reference.Address != 0 && _parents.TryAdd(reference.Address, address))
                    pending.Enqueue(reference.Address);
            }
        }
    }

    private void WritePath(TextWriter writer, ulong address)
    {
        var obj = _heap.GetObject(address);
        writer.WriteLine($"\n{address:x16} {Mib(obj.IsValid ? obj.Size : 0):F2} MiB {obj.Type?.Name ?? "<invalid object address>"}");
        if (!_parents.ContainsKey(address))
        {
            writer.WriteLine("  No strong-root path found in this snapshot.");
            return;
        }
        var chain = new List<ulong>();
        for (var current = address; current != 0; current = _parents[current]) chain.Add(current);
        writer.WriteLine($"  {_roots[chain[^1]]}");
        // Print both ends of very long paths without producing unbounded reports.
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (chain.Count > 24 && i == chain.Count - 13)
            {
                writer.WriteLine($"    ... {chain.Count - 24} intermediate objects ...");
                i = 11;
            }
            var item = _heap.GetObject(chain[i]);
            writer.WriteLine($"    -> {chain[i]:x16} {item.Type?.Name}");
        }
    }

    private static void Add<TKey>(Dictionary<TKey, TypeStat> stats, TKey key, ulong size, bool reachable) where TKey : notnull
    {
        if (!stats.TryGetValue(key, out var stat)) stats[key] = stat = new TypeStat();
        stat.Count++;
        stat.Size += size;
        if (reachable) stat.ReachableSize += size;
    }

    private static void WriteTypes(TextWriter writer, string title, IEnumerable<KeyValuePair<string, TypeStat>> stats)
    {
        writer.WriteLine($"\n=== {title} ===");
        writer.WriteLine($"{"Count",10} {"All MiB",10} {"Reachable",10} {"Unreached",10}  Type / group");
        foreach (var (name, stat) in stats)
            writer.WriteLine($"{stat.Count,10:N0} {Mib(stat.Size),10:F2} {Mib(stat.ReachableSize),10:F2} {Mib(stat.Size - stat.ReachableSize),10:F2}  {name}");
    }

    private static double Mib(ulong bytes) => bytes / 1048576.0;
    private sealed class TypeStat
    {
        internal long Count;
        internal ulong Size;
        internal ulong ReachableSize;
    }
    private sealed class SegmentStat
    {
        internal int Count;
        internal ulong Occupied;
        internal ulong Committed;
        internal ulong Free;
    }
}
