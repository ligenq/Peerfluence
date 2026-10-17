using System.Text;

namespace Peerfluence.DumpAnalyzer;

// MINIDUMP_MEMORY_INFO_LIST records VirtualQuery state, not resident working-set pages.
// Keep this separate from ClrMD: active GC segments omit committed, reusable GC regions.
internal sealed class WindowsMemoryMap(IReadOnlyList<WindowsMemoryMap.Region> regions)
{
    internal IReadOnlyList<Region> Regions { get; } = regions;
    private const uint Committed = 0x1000;
    private const uint Private = 0x20000;

    internal static WindowsMemoryMap? Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    internal static WindowsMemoryMap? Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (stream.Length < 32) return null;
        if (reader.ReadUInt32() != 0x504d444d) return null; // MDMP
        reader.ReadUInt32(); // version
        var count = reader.ReadUInt32();
        var directory = reader.ReadUInt32();
        RequireRange(stream, directory, (ulong)count * 12);
        for (uint i = 0; i < count; i++)
        {
            stream.Position = directory + (long)i * 12;
            var type = reader.ReadUInt32();
            var size = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            if (type != 16) continue; // MemoryInfoListStream
            RequireRange(stream, offset, size);
            if (size < 16) throw new InvalidDataException("Truncated memory-info header.");
            stream.Position = offset;
            var headerSize = reader.ReadUInt32();
            var entrySize = reader.ReadUInt32();
            var entries = reader.ReadUInt64();
            if (headerSize < 16 || headerSize > size || entrySize < 48 || entries > (size - headerSize) / entrySize)
                throw new InvalidDataException("Invalid memory-info list bounds.");
            var result = new List<Region>();
            for (ulong entry = 0; entry < entries; entry++)
            {
                stream.Position = checked((long)offset + headerSize + (long)(entry * entrySize));
                var start = reader.ReadUInt64();
                var allocationBase = reader.ReadUInt64();
                reader.ReadUInt32(); // allocation protection
                reader.ReadUInt32(); // alignment
                var length = reader.ReadUInt64();
                var state = reader.ReadUInt32();
                reader.ReadUInt32(); // protection
                var memoryType = reader.ReadUInt32();
                if (length > ulong.MaxValue - start)
                    throw new InvalidDataException("Memory-info region overflows the address space.");
                result.Add(new Region(start, start + length, allocationBase, state, memoryType));
            }
            return new WindowsMemoryMap(result);
        }
        return null;
    }

    internal void Write(TextWriter writer, (ulong Start, ulong End)[] gcSegments, int top)
    {
        var committed = Regions.Where(r => r.State == Committed).ToArray();
        writer.WriteLine(FormattableString.Invariant($"Windows committed private: {Mib(Total(committed.Where(r => r.Type == Private))):F2} MiB"));
        writer.WriteLine(FormattableString.Invariant($"Windows committed mapped:  {Mib(Total(committed.Where(r => r.Type == 0x40000))):F2} MiB"));
        writer.WriteLine(FormattableString.Invariant($"Windows committed images:  {Mib(Total(committed.Where(r => r.Type == 0x1000000))):F2} MiB"));
        var gcBases = committed.Where(r => r.Type == Private && gcSegments.Any(s => r.Start <= s.Start && s.Start < r.End))
            .Select(r => r.AllocationBase).ToHashSet();
        var privateGroups = committed.Where(r => r.Type == Private).GroupBy(r => r.AllocationBase)
            .Select(g => (Address: g.Key, Bytes: Total(g))).OrderByDescending(g => g.Bytes).ToArray();
        var gcCommitted = privateGroups.Where(g => gcBases.Contains(g.Address)).Aggregate(0UL, (sum, g) => sum + g.Bytes);
        var activeCommitted = committed.Where(r => r.Type == Private && gcBases.Contains(r.AllocationBase))
            .Aggregate(0UL, (sum, r) => sum + gcSegments.Aggregate(0UL, (overlap, s) =>
                overlap + (Math.Min(r.End, s.End) > Math.Max(r.Start, s.Start)
                    ? Math.Min(r.End, s.End) - Math.Max(r.Start, s.Start) : 0)));
        writer.WriteLine(FormattableString.Invariant($"Private commitment in GC allocation groups: {Mib(gcCommitted):F2} MiB"));
        writer.WriteLine(FormattableString.Invariant($"GC-group commitment outside active segments: {Mib(gcCommitted - activeCommitted):F2} MiB"));
        writer.WriteLine("GC groups include committed pages outside active segments; this is an address-space attribution.");
        writer.WriteLine(FormattableString.Invariant($"Largest {top} private allocation groups (not individual native allocations):"));
        foreach (var (address, bytes) in privateGroups.Take(top))
            writer.WriteLine(FormattableString.Invariant($"  {address:x16} {Mib(bytes),10:F2} MiB {(gcBases.Contains(address) ? "GC-related reservation" : "unattributed private memory")}"));
    }

    private static void RequireRange(Stream stream, ulong offset, ulong length)
    {
        if (offset > (ulong)stream.Length || length > (ulong)stream.Length - offset)
            throw new InvalidDataException("Minidump stream extends beyond the file.");
    }

    private static ulong Total(IEnumerable<Region> regions) => regions.Aggregate(0UL, (sum, r) => checked(sum + (r.End - r.Start)));
    private static double Mib(ulong bytes) => bytes / 1048576.0;
    internal readonly record struct Region(ulong Start, ulong End, ulong AllocationBase, uint State, uint Type);
}
