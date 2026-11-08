using System;
using System.IO;
using ValheimBroadcaster.Plugin.Streaming;

var marker = new ReviewMarker();
using var output = File.Create(args[0]);
bool flipped = args.Length > 1 && args[1] == "flip";
foreach (double time in new[] { 387.125, 404.75 })
{
    var frame = new byte[1280 * 720 * 4];
    Array.Fill(frame, (byte)100);
    if (!marker.Prepare("20261108T193012345-abcd1234", time)) throw new Exception("marker identity");
    marker.Composite(frame, 1280, 720, flipped);
    output.Write(frame);
}
if (marker.Prepare("OFF", 2)) throw new Exception("trace disabled");
