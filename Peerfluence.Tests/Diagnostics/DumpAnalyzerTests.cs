using System.Globalization;
using System.Text;
using Peerfluence.DumpAnalyzer;

namespace Peerfluence.Tests.Diagnostics;

public sealed class DumpAnalyzerTests
{
    [Fact]
    public void WindowsCommitment_ExcludesReservedPagesAndAccountsForInactiveGcRegions()
    {
        const ulong mib = 1048576;
        var map = new WindowsMemoryMap([
            new(0x1000000, 0x1000000 + 4 * mib, 0x1000000, 0x1000, 0x20000),
            new(0x2000000, 0x2000000 + 8 * mib, 0x1000000, 0x1000, 0x20000),
            new(0x3000000, 0x3000000 + 64 * 1024 * mib, 0x1000000, 0x2000, 0x20000),
            new(0x2000000000, 0x2000000000 + 2 * mib, 0x2000000000, 0x1000, 0x40000),
            new(0x3000000000, 0x3000000000 + 3 * mib, 0x3000000000, 0x1000, 0x1000000),
        ]);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        // A GC reservation can retain committed regions omitted from the active segment list.
        map.Write(writer, [(0x1000000, 0x1000000 + 4 * mib)], 20);

        var output = writer.ToString();
        Assert.Contains("Windows committed private: 12.00 MiB", output, StringComparison.Ordinal);
        Assert.Contains("Windows committed mapped:  2.00 MiB", output, StringComparison.Ordinal);
        Assert.Contains("Windows committed images:  3.00 MiB", output, StringComparison.Ordinal);
        Assert.Contains("Private commitment in GC allocation groups: 12.00 MiB", output, StringComparison.Ordinal);
        Assert.Contains("GC-group commitment outside active segments: 8.00 MiB", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsMemoryInfo_ReadsExtendedHeadersAndEntryStrides()
    {
        using var stream = MemoryInfoDump(headerSize: 24, entrySize: 56);
        var map = WindowsMemoryMap.Read(stream);

        var region = Assert.Single(Assert.IsType<WindowsMemoryMap>(map).Regions);
        Assert.Equal(new WindowsMemoryMap.Region(0x10000, 0x12000, 0x10000, 0x1000, 0x20000), region);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void WindowsMemoryInfo_ReportsAbsenceWithoutInventingZeroNativeUsage()
    {
        using var stream = MemoryInfoDump();
        stream.Position = 32;
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) writer.Write(3U);
        stream.Position = 0;

        Assert.Null(WindowsMemoryMap.Read(stream));
    }

    [Fact]
    public void WindowsMemoryInfo_RejectsTruncatedStreams()
    {
        using var stream = MemoryInfoDump();
        stream.SetLength(stream.Length - 1);

        Assert.Throws<InvalidDataException>(() => WindowsMemoryMap.Read(stream));
    }

    [Fact]
    public void WindowsMemoryInfo_RejectsEntryCountsOutsideTheStream()
    {
        using var stream = MemoryInfoDump();
        stream.Position = 52;
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) writer.Write(ulong.MaxValue);
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => WindowsMemoryMap.Read(stream));
    }

    [Fact]
    public void WindowsMemoryInfo_RejectsAddressOverflow()
    {
        using var stream = MemoryInfoDump();
        stream.Position = 60;
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) writer.Write(ulong.MaxValue);
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => WindowsMemoryMap.Read(stream));
    }

    private static MemoryStream MemoryInfoDump(uint headerSize = 16, uint entrySize = 48)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x504d444dU);
            writer.Write(0U);
            writer.Write(1U);
            writer.Write(32U);
            writer.Write(new byte[16]);
            writer.Write(16U);
            writer.Write(headerSize + entrySize);
            writer.Write(44U);
            writer.Write(headerSize);
            writer.Write(entrySize);
            writer.Write(1UL);
            writer.Write(new byte[headerSize - 16]);
            writer.Write(0x10000UL);
            writer.Write(0x10000UL);
            writer.Write(4U);
            writer.Write(0U);
            writer.Write(0x2000UL);
            writer.Write(0x1000U);
            writer.Write(4U);
            writer.Write(0x20000U);
            writer.Write(0U);
            writer.Write(new byte[entrySize - 48]);
        }
        stream.Position = 0;
        return stream;
    }
}
