"""Build a Linux test library with the pinned upstream PhysX 4.1 triangle kernels."""
import argparse
from pathlib import Path
import subprocess

PIN = "a2c0428acab643e60618c681b501e86f7fd558cc"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("sdk", type=Path, help="Checkout of NVIDIAGameWorks/PhysX at " + PIN)
parser.add_argument("--output", type=Path, default=Path("obj/native"))
args = parser.parse_args()
sdk = args.sdk.resolve()
head = subprocess.check_output(["git", "-C", sdk, "rev-parse", "HEAD"], text=True).strip()
if head != PIN:
    parser.error(f"PhysX SDK must be at {PIN}; found {head}")
src = sdk / "physx/source"
gu = src / "geomutils/src"
includes = []
for root in (src / "common", src / "foundation", src / "geomutils", sdk / "physx/include", sdk / "pxshared/include"):
    for path in (root, *sorted(p for p in root.rglob("*") if p.is_dir())):
        includes.append("-I" + str(path))
args.output.mkdir(parents=True, exist_ok=True)
# Export only the test ABI so the linker can discard unrelated mesh/scene entry points.
symbols = args.output / "native-symbols.map"
symbols.write_text("{global: terrain_height; terrain_overlap; terrain_clearance; terrain_triangle; local: *;};\n")
subprocess.run(["g++", "-std=c++14", "-O2", "-DNDEBUG", "-DPX_PHYSX_STATIC_LIB", "-msse2",
                "-Wno-deprecated-declarations", "-fPIC", "-ffunction-sections", "-fdata-sections",
                "-shared", *includes, Path(__file__).with_name("Terrain.cpp"),
                gu / "sweep/GuSweepSphereTriangle.cpp", gu / "sweep/GuSweepTriangleUtils.cpp",
                gu / "intersection/GuIntersectionRaySphere.cpp", gu / "intersection/GuIntersectionRayCapsule.cpp",
                gu / "distance/GuDistancePointTriangle.cpp", "-Wl,--gc-sections", "-Wl,--no-undefined",
                "-Wl,--version-script=" + str(symbols), "-o", args.output / "libjourneyterrain.so"], check=True)
