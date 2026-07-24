#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 --version VERSION [--rid linux-x64|linux-arm64] [--configuration Release] [--output DIR] [--no-restore] [--appimage]"
}

version=""
rid="linux-x64"
configuration="Release"
output_root=""
restore_arg=""
build_appimage=false

while (($#)); do
    case "$1" in
        --version) version="${2:-}"; shift 2 ;;
        --rid) rid="${2:-}"; shift 2 ;;
        --configuration) configuration="${2:-}"; shift 2 ;;
        --output) output_root="${2:-}"; shift 2 ;;
        --no-restore) restore_arg="--no-restore"; shift ;;
        --appimage) build_appimage=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
    echo "--version must be a semantic version" >&2
    exit 2
fi

case "$rid" in
    linux-x64) deb_arch="amd64" ;;
    linux-arm64) deb_arch="arm64" ;;
    *) echo "Unsupported Linux RID: $rid" >&2; exit 2 ;;
esac

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
output_root=${output_root:-"$repo_root/artifacts/linux/$rid"}
output_root=$(realpath -m -- "$output_root")
if [[ "$output_root" == "/" || "$output_root" == "$repo_root" ]]; then
    echo "Refusing unsafe output directory: $output_root" >&2
    exit 2
fi
publish_dir="$output_root/publish"
app_dir="$output_root/Peerfluence.AppDir"
package_root="$output_root/deb-root"
artifact_base="Peerfluence-$version-$rid"

rm -rf -- "$publish_dir" "$app_dir" "$package_root"
mkdir -p "$publish_dir" "$app_dir/usr/lib/peerfluence" "$app_dir/usr/bin" \
    "$app_dir/usr/share/applications" "$app_dir/usr/share/metainfo" \
    "$app_dir/usr/share/icons/hicolor/386x386/apps"

dotnet publish "$repo_root/Peerfluence/Peerfluence.csproj" \
    --configuration "$configuration" --runtime "$rid" --self-contained true \
    --output "$publish_dir" ${restore_arg:+$restore_arg} \
    -p:Version="$version" -p:PublishAot=false -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

test -x "$publish_dir/Peerfluence"
find "$publish_dir" -type f -name '*.pdb' -delete
cp -a "$publish_dir/." "$app_dir/usr/lib/peerfluence/"
cp "$script_dir/linux/io.github.ligenq.Peerfluence.desktop" "$app_dir/usr/share/applications/"
cp "$script_dir/linux/io.github.ligenq.Peerfluence.metainfo.xml" "$app_dir/usr/share/metainfo/"
cp "$repo_root/Peerfluence/Assets/application-icon.png" \
    "$app_dir/usr/share/icons/hicolor/386x386/apps/io.github.ligenq.Peerfluence.png"
cp "$script_dir/linux/AppRun" "$app_dir/AppRun"
cp "$repo_root/Peerfluence/Assets/application-icon.png" "$app_dir/io.github.ligenq.Peerfluence.png"
ln -s ../lib/peerfluence/Peerfluence "$app_dir/usr/bin/peerfluence"
chmod +x "$app_dir/AppRun" "$app_dir/usr/lib/peerfluence/Peerfluence"
find "$app_dir" -type d -exec chmod 755 {} +
find "$app_dir" -type f ! -name AppRun ! -name Peerfluence -exec chmod 644 {} +

tar -C "$app_dir" -czf "$output_root/$artifact_base.tar.gz" .

mkdir -p "$package_root/opt/peerfluence" "$package_root/usr/bin" \
    "$package_root/usr/share/applications" "$package_root/usr/share/metainfo" \
    "$package_root/usr/share/icons/hicolor/386x386/apps" "$package_root/DEBIAN"
cp -a "$publish_dir/." "$package_root/opt/peerfluence/"
cp "$script_dir/linux/io.github.ligenq.Peerfluence.desktop" "$package_root/usr/share/applications/"
cp "$script_dir/linux/io.github.ligenq.Peerfluence.metainfo.xml" "$package_root/usr/share/metainfo/"
cp "$repo_root/Peerfluence/Assets/application-icon.png" \
    "$package_root/usr/share/icons/hicolor/386x386/apps/io.github.ligenq.Peerfluence.png"
ln -s /opt/peerfluence/Peerfluence "$package_root/usr/bin/peerfluence"
find "$package_root" -type d -exec chmod 755 {} +
find "$package_root" -type f ! -name Peerfluence -exec chmod 644 {} +
chmod 755 "$package_root/opt/peerfluence/Peerfluence"
installed_size=$(du -sk "$package_root/opt/peerfluence" | cut -f1)
sed -e "s/@VERSION@/$version/g" -e "s/@ARCH@/$deb_arch/g" -e "s/@SIZE@/$installed_size/g" \
    "$script_dir/linux/debian-control.in" > "$package_root/DEBIAN/control"
dpkg-deb --root-owner-group --build "$package_root" "$output_root/$artifact_base.deb"

if $build_appimage; then
    if ! command -v appimagetool >/dev/null 2>&1; then
        echo "--appimage requested but appimagetool is not on PATH" >&2
        exit 1
    fi
    appimagetool "$app_dir" "$output_root/$artifact_base.AppImage"
fi

echo "Linux artifacts written to $output_root"
