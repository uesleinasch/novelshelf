#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
version="$(grep -oP '(?<=<Version>)[^<]+' "$root/Directory.Build.props")"
out="${OUT_DIR:-$root/artifacts}"
work="$out/appimage"
appdir="$work/NovelShelf.AppDir"
runtime="${APPIMAGE_RUNTIME:-}"

rm -rf "$work"
mkdir -p "$appdir/usr/lib/novelshelf" "$appdir/usr/share/applications" "$appdir/usr/share/icons/hicolor/scalable/apps"

dotnet publish "$root/src/NovelShelf.App" -c Release -r linux-x64 --self-contained \
  -p:DebugType=None -o "$appdir/usr/lib/novelshelf"

cp "$root/packaging/linux/novelshelf.desktop" "$appdir/novelshelf.desktop"
cp "$root/packaging/linux/novelshelf.desktop" "$appdir/usr/share/applications/"
cp "$root/packaging/linux/novelshelf.svg" "$appdir/novelshelf.svg"
cp "$root/packaging/linux/novelshelf.svg" "$appdir/usr/share/icons/hicolor/scalable/apps/"
ln -s novelshelf.svg "$appdir/.DirIcon"

cat > "$appdir/AppRun" <<'RUN'
#!/bin/sh
here="$(dirname "$(readlink -f "$0")")"
exec "$here/usr/lib/novelshelf/NovelShelf" "$@"
RUN
chmod +x "$appdir/AppRun"

if [ -z "$runtime" ]; then
  runtime="$work/runtime-x86_64"
  curl -fsSL -o "$runtime" https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-x86_64
fi

mksquashfs "$appdir" "$work/app.squashfs" -root-owned -noappend -comp zstd -quiet
target="$out/NovelShelf-v$version-x86_64.AppImage"
cat "$runtime" "$work/app.squashfs" > "$target"
chmod +x "$target"
echo "$target"
