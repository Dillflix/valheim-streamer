#!/usr/bin/env bash
set -eu
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
for mode in auto flip; do
    dotnet run --project tests/stream-review/MarkerFrames.csproj -c Release -- "$work/source.rgba" "$mode"
    filter=scale=426:240
    if [ "$mode" = flip ]; then filter=vflip,$filter; fi
    ffmpeg -v error -y -f rawvideo -s 1280x720 -pix_fmt rgba -r 1 -i "$work/source.rgba" -vf "$filter" -pix_fmt yuv420p -c:v libx264 -f hls -hls_time 1 "$work/feed.m3u8"
    ffmpeg -v error -y -i "$work/feed.m3u8" -vsync 0 -f rawvideo -pix_fmt gray "$work/frame.gray"
    node tests/stream-review/decoded.mjs "$work/frame.gray" 426 240
done
