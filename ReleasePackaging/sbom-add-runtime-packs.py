#!/usr/bin/env python3
"""Adds the .NET runtime packs to a CycloneDX bill of materials.

Peerfluence publishes self-contained: the .NET runtime is inside the artifact rather than on the
machine that runs it, so a CVE in the runtime is fixed by a new Peerfluence release and by nothing
else. That makes the runtime the single most important thing for the bill of materials to name.

It is also the one thing the generator misses. NuGet resolves runtime packs as download
dependencies - fetched, unpacked, and used by the publish, but never referenced by the compilation -
so they appear in project.assets.json under `downloadDependencies` and not in the `targets` and
`libraries` sections a bill-of-materials generator reads. Without this step the file lists Avalonia,
SukiUI and PeerSharp accurately and says nothing whatsoever about the sixty megabytes of runtime
shipped beside them.
"""

from __future__ import annotations

import argparse
import json
import re
import sys

# downloadDependencies pin an exact version as an interval: "[10.0.11, 10.0.11]".
VERSION_INTERVAL = re.compile(r"[\[(]\s*(?P<version>[^,\])\s]+)")


def read_runtime_packs(assets_path: str) -> list[dict[str, str]]:
    with open(assets_path, encoding="utf-8-sig") as handle:
        assets = json.load(handle)

    packs: dict[str, str] = {}
    frameworks = assets.get("project", {}).get("frameworks", {})
    for framework in frameworks.values():
        for dependency in framework.get("downloadDependencies", []):
            name = dependency.get("name")
            match = VERSION_INTERVAL.match(dependency.get("version", ""))
            if name and match:
                packs[name] = match.group("version")

    return [{"name": name, "version": packs[name]} for name in sorted(packs)]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bom", required=True, help="CycloneDX JSON file to add components to")
    parser.add_argument("--assets", required=True, help="project.assets.json from the RID restore")
    args = parser.parse_args()

    with open(args.bom, encoding="utf-8") as handle:
        bom = json.load(handle)

    components = bom.setdefault("components", [])
    known = {(component.get("name"), component.get("version")) for component in components}

    added = 0
    for pack in read_runtime_packs(args.assets):
        if (pack["name"], pack["version"]) in known:
            continue

        reference = f"pkg:nuget/{pack['name']}@{pack['version']}"
        components.append({
            "type": "library",
            "bom-ref": reference,
            "name": pack["name"],
            "version": pack["version"],
            "purl": reference,
            "scope": "required",
            "description": "Runtime pack published inside the self-contained application.",
        })
        added += 1

    if added == 0:
        # Never silently produce a bill of materials missing the thing it exists to record.
        print(f"error: no runtime packs found in {args.assets}", file=sys.stderr)
        return 1

    with open(args.bom, "w", encoding="utf-8") as handle:
        json.dump(bom, handle, indent=2)
        handle.write("\n")

    print(f"Added {added} runtime pack(s) to {args.bom}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
