#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 --version VERSION --rid linux-x64|linux-arm64 --tarball Peerfluence-VERSION-RID.tar.gz [--output DIR]"
}

# Packs a snap from a Linux release tarball, the AppDir that build-linux.sh makes, rather than from
# source: the snap then holds the same build as the .deb and .tar.gz, and a version already released
# can still be packed.
#
# Snapcraft runs in destructive mode, on this host rather than in a container. That needs an Ubuntu
# 24.04 host to match the snap's core24 base, of the architecture being packed, and root to install
# what the GNOME extension builds with. The workflow's runners are all three.

version=""
rid=""
tarball=""
output_root=""

while (($#)); do
    case "$1" in
        --version) version="${2:-}"; shift 2 ;;
        --rid) rid="${2:-}"; shift 2 ;;
        --tarball) tarball="${2:-}"; shift 2 ;;
        --output) output_root="${2:-}"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
    echo "--version must be a semantic version" >&2
    exit 2
fi

case "$rid" in
    linux-x64) snap_arch="amd64" ;;
    linux-arm64) snap_arch="arm64" ;;
    *) echo "Unsupported Linux RID: $rid" >&2; exit 2 ;;
esac

if [[ ! -f "$tarball" ]]; then
    echo "--tarball must name a release tarball" >&2
    exit 2
fi

if ! command -v snapcraft >/dev/null 2>&1; then
    echo "snapcraft is not on PATH" >&2
    exit 1
fi

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
tarball=$(realpath -- "$tarball")
output_root=${output_root:-"$repo_root/artifacts/snap/$rid"}
output_root=$(realpath -m -- "$output_root")
if [[ "$output_root" == "/" || "$output_root" == "$repo_root" ]]; then
    echo "Refusing unsafe output directory: $output_root" >&2
    exit 2
fi

project="$output_root/project"
rm -rf -- "$project"
mkdir -p "$project/snap" "$project/appdir"
tar -xzf "$tarball" -C "$project/appdir"
test -x "$project/appdir/usr/lib/peerfluence/Peerfluence"
sed -e "s/@VERSION@/$version/g" -e "s/@SNAP_ARCH@/$snap_arch/g" \
    "$script_dir/linux/snapcraft.yaml.in" > "$project/snap/snapcraft.yaml"

sudo_cmd=()
if ((EUID != 0)); then
    sudo_cmd=(sudo)
fi
snap_file="$output_root/peerfluence_${version}_${snap_arch}.snap"
(cd "$project" && "${sudo_cmd[@]}" snapcraft pack --destructive-mode --output "$snap_file")
test -f "$snap_file"

echo "Snap written to $snap_file"
