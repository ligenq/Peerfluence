#!/usr/bin/env bash
set -euo pipefail

# Produces a CycloneDX bill of materials for one runtime identifier.
#
# Generated from the restore graph rather than from the published files, and deliberately. Both
# release builds produce a single opaque executable - Windows compiles ahead of time, Linux bundles
# every assembly into one file - so a scanner pointed at the published output finds one binary and
# learns nothing. The dependency graph is where the answer actually is.

usage() {
    echo "Usage: $0 --version VERSION --rid RID [--configuration Release] [--output DIR]"
}

version=""
rid=""
configuration="Release"
output_root=""

while (($#)); do
    case "$1" in
        --version) version="${2:-}"; shift 2 ;;
        --rid) rid="${2:-}"; shift 2 ;;
        --configuration) configuration="${2:-}"; shift 2 ;;
        --output) output_root="${2:-}"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

test -n "$version" || { echo "--version is required" >&2; usage >&2; exit 2; }
test -n "$rid" || { echo "--rid is required" >&2; usage >&2; exit 2; }

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
project="$repo_root/Peerfluence/Peerfluence.csproj"
output_root="${output_root:-$repo_root/artifacts/sbom}"
filename="peerfluence-$version-$rid.cdx.json"

mkdir -p "$output_root"

# Restored here rather than left to the generator, so the assets file the next step reads is the one
# for this runtime identifier and not whatever a previous restore left behind.
dotnet restore "$project" --runtime "$rid"

dotnet-CycloneDX "$project" \
    --runtime "$rid" \
    --framework net10.0 \
    --output "$output_root" \
    --filename "$filename" \
    --output-format Json \
    --set-name Peerfluence \
    --set-version "$version" \
    --disable-package-restore

# python3 on the build runner, python on a Windows developer machine where that is the only name.
# Each candidate is run rather than merely located: Windows puts a stub on the path under both names
# that exists, resolves, and does nothing but advertise the Microsoft Store.
python_bin=""
for candidate in python3 python; do
    if "$candidate" -c "import sys; sys.exit(0)" >/dev/null 2>&1; then
        python_bin="$candidate"
        break
    fi
done
test -n "$python_bin" || { echo "python is required but was not found" >&2; exit 1; }

"$python_bin" "$script_dir/sbom-add-runtime-packs.py" \
    --bom "$output_root/$filename" \
    --assets "$repo_root/Peerfluence/obj/project.assets.json"

components=$("$python_bin" -c "import json,sys; print(len(json.load(open(sys.argv[1], encoding='utf-8')).get('components', [])))" "$output_root/$filename")
echo "Wrote $output_root/$filename ($components components, $configuration/$rid)"
