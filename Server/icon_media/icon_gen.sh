#!/bin/bash
set -e
cd -- "$(dirname -- "$0")"

ICO="walkuo.ico"
IMG="walkuo-512.png"
WORKDIR=$(mktemp -d "./.icon-gen.XXXXXX")
trap 'rm -rf -- "$WORKDIR"' EXIT

ICONS=()
for SIZE in 256 128 64 48 32 24 16; do
    ICON="$WORKDIR/icon-$SIZE.png"
    convert "$IMG" -resize "${SIZE}x${SIZE}" "$ICON"
    ICONS+=("$ICON")
done

convert "${ICONS[@]}" "$WORKDIR/$ICO"
mv -fT -- "$WORKDIR/$ICO" "../$ICO"
