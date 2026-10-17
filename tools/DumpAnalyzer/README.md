# DumpAnalyzer

Local, read-only analysis of managed process dumps using ClrMD. Uses the repository's .NET 10 SDK.

```powershell
dotnet run --project tools/DumpAnalyzer/DumpAnalyzer.csproj -- C:\temp\Peerfluence.DMP
dotnet run --project tools/DumpAnalyzer/DumpAnalyzer.csproj -- C:\temp\Peerfluence.DMP --top 30 --root 0x12345678
```

Redirect stdout to save a report; progress and errors go to stderr. `--top` bounds tables and the
number of large-object root paths (1–100). Repeat `--root` to inspect additional object addresses.
The report prints type names, sizes and addresses, without dumping strings or buffer contents.
Missing/unwalkable heaps and multiple CLR runtimes fail explicitly instead of presenting partial
results as a complete diagnosis. Use `dotnet-dump analyze` for multi-runtime selection and deeper SOS
investigation. Native AOT dumps do not expose the CoreCLR heap required by this tool.

The report distinguishes:

- Windows private, mapped and image commitment, when the dump includes `MemoryInfoListStream`.
  Reserved address space is excluded. These values are not resident working-set measurements.
- Private allocation groups that contain active GC segments. These can include committed pages
  outside the active segments: looking only at `eeheap -gc` can miss that part of the footprint.
  Grouping by Windows allocation base is address-space attribution, not a native allocation trace.
- Active segment occupied ranges, commitment, and free blocks, with frozen segments listed separately.
  Free blocks are classified by the actual segment, not by the size of a block.
- Objects reachable from strong GC roots versus objects not reached from those roots. Traversal
  includes pinned/stack/handle/finalizer roots, dependent handles and frozen objects. Weak references
  do not keep their targets alive. A direct parent alone does not establish liveness.
- Physical and reachable shallow sizes by type, exact byte-array sizes, first-parent groups, and
  bounded root paths for the largest objects.

Unreached objects may be awaiting collection. Reachable buffers may be useful working data or caches.
Neither is proof of a leak. Reachable size is not exclusive retained size: this tool does not compute
dominators. A single dump cannot establish an allocation rate or whether memory grows without bound.
Native heap owners and graphics allocations require additional native diagnostics or allocation traces.

Regression tests for Windows memory accounting and malformed streams run with the main unit suite:

```powershell
dotnet test --project Peerfluence.Tests/Peerfluence.Tests.csproj -- --filter-class '*DumpAnalyzerTests'
```
