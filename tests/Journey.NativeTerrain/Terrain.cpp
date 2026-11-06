// Use the actual BV4 sphere/triangle leaf predicate and the sphere/triangles sweep
// from the upstream SDK. Supply our own small heightfield neighbourhood as broadphase.
#include "GuBV4_SphereOverlap.cpp"
#include "GuSweepSphereTriangle.h"
#include <algorithm>
#include <cmath>
#include <vector>

using Triangles = std::vector<PxTriangle>;
constexpr float Radius = 0.82f;
constexpr float GridStep = 2.0f;
constexpr float Skin = 0.04f;
constexpr float RecordedZ = -792.59f;

static PxVec3 vertex(int i, int j)
{
    const float x = i * GridStep;
    const float z = j * GridStep;
    const float dz = z - RecordedZ;
    const float contactSlope = std::max(-22.f, std::min(42.f, 1.3f * dz));
    const float ridge = std::max(0.f, 105.f - std::abs(dz - 680.f) * 0.95f);
    const float mountain = std::max(0.f, 125.f - std::abs(dz - 1380.f) * 1.30f);
    const float shoulder = std::min(15.f, std::abs(x + 1730.37f) * .20f) *
        std::min(1.f, std::max(0.f, (dz - 35.f) / 12.f));
    return PxVec3(x, 138.6f + contactSlope + ridge + mountain + shoulder, z);
}

static PxTriangle face(int i, int j, int faceIndex)
{
    const auto a = vertex(i, j), b = vertex(i, j + 1), c = vertex(i + 1, j);
    return faceIndex == 0 ? PxTriangle(a, b, c) : PxTriangle(c, b, vertex(i + 1, j + 1));
}

static Triangles nearbyFaces(PxVec3 a, PxVec3 b)
{
    Triangles faces;
    const int minI = int(std::floor((std::min(a.x, b.x) - Radius) / GridStep));
    const int minJ = int(std::floor((std::min(a.z, b.z) - Radius) / GridStep));
    const int maxI = int(std::floor((std::max(a.x, b.x) + Radius) / GridStep));
    const int maxJ = int(std::floor((std::max(a.z, b.z) + Radius) / GridStep));
    for (int j = minJ; j <= maxJ; ++j)
        for (int i = minI; i <= maxI; ++i)
        {
            faces.push_back(face(i, j, 0));
            faces.push_back(face(i, j, 1));
        }
    return faces;
}

extern "C" float terrain_height(float x, float z)
{
    const int i = int(std::floor(x / GridStep)), j = int(std::floor(z / GridStep));
    const float u = x / GridStep - i, v = z / GridStep - j;
    const auto a = vertex(i, j), b = vertex(i, j + 1), c = vertex(i + 1, j), d = vertex(i + 1, j + 1);
    if (u + v <= 1.f) return a.y + u * (c.y - a.y) + v * (b.y - a.y);
    return d.y + (1 - u) * (b.y - d.y) + (1 - v) * (c.y - d.y);
}

static bool overlaps(const Triangles& faces, PxVec3 center)
{
    SphereParams params{};
    params.mCenter_PaddedAligned = center;
    params.mRadius2 = Radius * Radius;
    for (const auto& triangle : faces)
        if (__SphereTriangle(&params, triangle.verts[0], triangle.verts[1], triangle.verts[2]))
            return true;
    return false;
}

extern "C" int terrain_overlap(float x, float y, float z)
{
    const PxVec3 position(x, y, z);
    return overlaps(nearbyFaces(position, position), position);
}

static float sweep(const Triangles& faces, PxVec3 from, PxVec3 to)
{
    PxVec3 direction = to - from;
    const float length = direction.normalize();
    if (length == 0) return 0;
    PxSweepHit hit;
    PxVec3 normal;
    const bool blocked = Gu::sweepSphereTriangles(PxU32(faces.size()), faces.data(), from,
        Radius, direction, length, nullptr, hit, normal, true, true, false, true);
    return blocked ? std::max(0.f, hit.distance - Skin) : length;
}

extern "C" float terrain_clearance(float x, float y, float z, float endX, float endY, float endZ,
    int allowVerticalRelease, int addRoof, float roofY)
{
    const PxVec3 from(x, y, z), to(endX, endY, endZ);
    const auto faces = nearbyFaces(from, to);
    const bool initialOverlap = overlaps(faces, from);
    if (initialOverlap && !allowVerticalRelease) return 0;
    float limit = initialOverlap ? (to - from).magnitude() : sweep(faces, from, to);

    if (addRoof)
    {
        const float size = 25.f;
        const PxVec3 a(x-size, roofY, z-size), b(x+size, roofY, z-size);
        const PxVec3 c(x-size, roofY, z+size), d(x+size, roofY, z+size);
        limit = std::min(limit, sweep({PxTriangle(a,c,b), PxTriangle(b,c,d)}, from, to));
    }
    return limit;
}

extern "C" void terrain_triangle(int i, int j, int faceIndex, float* coords)
{
    const auto f = face(i, j, faceIndex);
    for (int corner = 0; corner < 3; ++corner)
    {
        coords[3*corner] = f.verts[corner].x;
        coords[3*corner+1] = f.verts[corner].y;
        coords[3*corner+2] = f.verts[corner].z;
    }
}
