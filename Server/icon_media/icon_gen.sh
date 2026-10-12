#!/bin/bash
set -e

cd -- "$(dirname -- "$0")"

ICO="walkuo.ico"
IMG="walkuo-512.png"
WORKDIR=$(mktemp -d "./.icon-gen.XXXXXX")
trap 'rm -rf -- "$WORKDIR"' EXIT

ICONS=()

# 32 bpp: Windows XP through Windows 11, with smooth alpha transparency.
for SIZE in 256 48 32 16; do
    ICON="$WORKDIR/icon-${SIZE}-32.png"

    magick "$IMG" \
        -resize "${SIZE}x${SIZE}" \
        "PNG32:$ICON"

    ICONS+=("$ICON")
done

# 8 bpp: Windows 9x/Me systems using 256-color display modes.
for SIZE in 48 32 16; do
    ICON="$WORKDIR/icon-${SIZE}-8.png"

    magick "$IMG" \
        -resize "${SIZE}x${SIZE}" \
        -colors 256 \
        -type Palette \
        "$ICON"

    ICONS+=("$ICON")
done

# 4 bpp: maximum compatibility with older Windows 95/98 display modes.
for SIZE in 48 32 16; do
    ICON="$WORKDIR/icon-${SIZE}-4.png"

    magick "$IMG" \
        -resize "${SIZE}x${SIZE}" \
        -colors 16 \
        -type Palette \
        "$ICON"

    ICONS+=("$ICON")
done

# Store all icon images as classic DIB/BMP data inside the ICO.
# This avoids PNG-in-ICO and provides the broadest compatibility
# from Windows 9x through Windows 11.
magick "${ICONS[@]}" \
    -compress none \
    "$WORKDIR/$ICO"

mv -vfT -- "$WORKDIR/$ICO" "../$ICO"
