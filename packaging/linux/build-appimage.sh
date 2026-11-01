#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
csproj="$root/Legendary Sharp/Legendary Sharp.csproj"
version="$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$csproj" | head -n 1)"
binary="$root/publish/linux/LegendarySharp"
appdir="$root/publish/AppDir"
output="$root/publish/LegendarySharp-v$version-x86_64.AppImage"
icons="$appdir/usr/share/icons/hicolor/256x256/apps"

export ARCH=x86_64

if command -v podman >/dev/null 2>&1; then
  engine="podman"
  owner="0:0"
elif command -v docker >/dev/null 2>&1; then
  engine="docker"
  owner="$(id -u):$(id -g)"
else
  echo "podman or docker is required to build the AppImage" >&2
  exit 1
fi

mkdir -p "$root/publish"

"$engine" run --rm -i \
  -v "$root:/src:Z" \
  -e OWNER="$owner" \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_NOLOGO=1 \
  -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
  -e DEBIAN_FRONTEND=noninteractive \
  ubuntu:22.04 bash -s <<'CONTAINER'
set -euo pipefail
trap 'chown -R "$OWNER" /src/publish "/src/Legendary Sharp/bin" "/src/Legendary Sharp/obj" 2>/dev/null || true' EXIT

apt-get update </dev/null
apt-get install -y --no-install-recommends clang zlib1g-dev curl ca-certificates </dev/null

curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /opt/dotnet

export PATH="/opt/dotnet:$PATH"
dotnet publish "/src/Legendary Sharp/Legendary Sharp.csproj" -c Release -r linux-x64 -p:NativePublishPass=true -o /src/publish/linux </dev/null
CONTAINER

rm -rf "$appdir"
mkdir -p "$appdir/usr/bin" "$icons"

cp "$binary" "$appdir/usr/bin/"
cp "$root/assets/icon.png" "$appdir/legendarysharp.png"
cp "$root/assets/icon.png" "$icons/legendarysharp.png"
cp "$root/installer/legendarysharp.desktop" "$appdir/"

cat >"$appdir/AppRun" <<'RUN'
#!/bin/sh
bin="$(dirname "$(readlink -f "$0")")/usr/bin/LegendarySharp"

if [ ! -t 0 ]; then
  for term in x-terminal-emulator konsole gnome-terminal kitty alacritty xterm; do
    if command -v "$term" >/dev/null 2>&1; then
      case "$term" in
        gnome-terminal) exec "$term" -- "$bin" "$@" ;;
        kitty) exec "$term" "$bin" "$@" ;;
        *) exec "$term" -e "$bin" "$@" ;;
      esac
    fi
  done
fi

exec "$bin" "$@"
RUN

chmod +x "$appdir/AppRun" "$appdir/usr/bin/LegendarySharp"

if command -v appimagetool >/dev/null 2>&1; then
  tool="appimagetool"
else
  tool="$root/tools/appimagetool-x86_64.AppImage"
  if [ ! -x "$tool" ]; then
    mkdir -p "$root/tools"
    curl -fL -o "$tool" "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "$tool"
  fi
fi

APPIMAGE_EXTRACT_AND_RUN=1 "$tool" "$appdir" "$output"
