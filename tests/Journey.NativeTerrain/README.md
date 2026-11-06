# Native Journey terrain lab

This optional Linux x86-64 test drives the production Journey guide and terrain-contact policy through a reconstructed mountain course. It calls NVIDIA PhysX 4.1's native BV4 sphere/triangle overlap leaf and sphere/triangles sweep kernels. The harness supplies a regular triangle grid as the local broadphase. It does not launch Unity, use Valheim world data, or record game footage.

The starting position, camera radius, tile bounds and 4 m vertical proposal come from the 0.8.58 trace. The hillside triangles, subsequent ridges, and roof control are constructed. Cases cover the recorded tile-box rule, a contact footprint on either side of the tile boundary, an independent overhead obstruction, and an autonomous trip to a destination 2,045 m away.

Use GCC, .NET 8, Python 3, and the pinned SDK checkout:

```sh
git clone --depth 1 --branch 4.1 https://github.com/NVIDIAGameWorks/PhysX.git /tmp/journey-physx
git -C /tmp/journey-physx rev-parse HEAD
# Expected: a2c0428acab643e60618c681b501e86f7fd558cc
cd tests/Journey.NativeTerrain
python3 build-native.py /tmp/journey-physx
LD_LIBRARY_PATH="$PWD/obj/native" dotnet run -c Release
```

The native library is test-only. All SDK sources retain their upstream license and stay in the external checkout. The client and server packages do not include the lab.
