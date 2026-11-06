using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class JourneyGuideWorld : IJourneyTerrainContact
{
    // Valheim rebuilds this mesh in place after terrain edits. Read the current
    // collider vertices and indices for each contact frame; never cache heights
    // across frames or substitute world-generation heights for loaded geometry.
    private static readonly FieldInfo? nativeTerrainMesh = typeof(Heightmap).GetField("m_collisionMesh",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private readonly List<Vector3> terrainVertices = new List<Vector3>();
    private readonly List<int> terrainIndices = new List<int>();
    private readonly MeshCollider[] footprintTiles = new MeshCollider[4];
    private readonly JourneyTerrainClearanceHistory terrainHistory = new JourneyTerrainClearanceHistory();
    internal void PresentedTerrainPose(int frame, AutonomyPosition actual) => terrainHistory.Presented(frame, actual);
    private int terrainFrame = -1, terrainTiles;
    private Vector3 terrainPosition;
    private double centreGround, requiredHeight;
    private bool terrainProved;
    private string terrainReason = "none";
    private Vector3 proofTile;
    internal string LastTerrainContact => terrainFrame < 0 ? "none" :
        $"frame {terrainFrame}; at {terrainPosition:F3}; {terrainReason}; tile {proofTile:F2}; {terrainTiles} tiles; ground {centreGround:F3}; sphere radius {radius:F3}; clear centre Y {requiredHeight:F3}";

    public bool NeedsVerticalEscape(AutonomyPosition position)
    {
        if (!BindFrame() || !CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Live, out var work)) return false;
        using (work)
        {
            var start = new Vector3(position.X, position.Y, position.Z);
            if (!ScenicSurface.TryGroundHeight(start, out float ground)) return false;
            // A steep face may reach the side of the sphere even while ground
            // directly underneath is much lower. Check the real overlap too.
            if (start.y > ground + radius + ContactSkin &&
                !Physics.CheckSphere(start, radius + ContactSkin, terrainMask, QueryTriggerInteraction.Ignore)) return false;
            return MeasureTerrain(start) && start.y < requiredHeight + .002 &&
                JourneyTerrainContactPolicy.CanRise(position, new AutonomyPosition(position.X, position.Y + 1, position.Z),
                    radius + ContactSkin, centreGround, requiredHeight, terrainHistory.CanRecoverAt(Time.frameCount, position));
        }
    }

    private bool AllowsTerrainRise(Vector3 start, Vector3 end)
    {
        if (start.x != end.x || start.z != end.z || end.y <= start.y || !MeasureTerrain(start)) return false;
        var from = new AutonomyPosition(start.x, start.y, start.z);
        bool rise = JourneyTerrainContactPolicy.CanRise(from,
            new AutonomyPosition(end.x, end.y, end.z), radius + ContactSkin, centreGround, requiredHeight,
            terrainHistory.CanRecoverAt(Time.frameCount, from));
        terrainReason = rise ? "vertical terrain clearance verified" : "vertical terrain clearance denied";
        return rise;
    }

    private bool ProvedTile(Collider solid)
    {
        for (int i = 0; i < terrainTiles; i++)
            if (footprintTiles[i] == solid) return true;
        return false;
    }

    private bool MeasureTerrain(Vector3 start)
    {
        if (terrainFrame == Time.frameCount && terrainPosition.Equals(start)) return terrainProved;
        terrainFrame = Time.frameCount;
        terrainPosition = start;
        terrainTiles = 0;
        terrainProved = false;
        centreGround = double.NaN;
        requiredHeight = double.NegativeInfinity;
        proofTile = default;
        Heightmap? centre = ScenicSurface.OutdoorMap(start);
        if (!centre || !TerrainGrid(centre!) || !ScenicSurface.TryGroundHeight(start, out float measured))
            return TerrainFailure("missing centre tile/ground");
        centreGround = measured;
        proofTile = centre.transform.position;
        float span = centre!.m_width * centre.m_scale;
        Vector3 origin = centre.transform.position - new Vector3(span * .5f, 0, span * .5f);
        float r = radius + ContactSkin;
        if (2 * r >= span || start.x < origin.x || start.x > origin.x + span ||
            start.z < origin.z || start.z > origin.z + span) return TerrainFailure("unexpected tile extent");
        int minX = Mathf.FloorToInt((start.x - r - origin.x) / span);
        int maxX = Mathf.FloorToInt((start.x + r - origin.x) / span);
        int minZ = Mathf.FloorToInt((start.z - r - origin.z) / span);
        int maxZ = Mathf.FloorToInt((start.z + r - origin.z) / span);
        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            // Resolve a tile by its interior rather than an ambiguous seam.
            Vector3 tileCentre = centre.transform.position + new Vector3(x * span, 0, z * span);
            Heightmap? map = x == 0 && z == 0 ? centre : ScenicSurface.OutdoorMap(tileCentre);
            if (!map || !TerrainGrid(map!) || Mathf.Abs(map!.m_width * map.m_scale - span) > .001f ||
                Mathf.Abs(map.transform.position.x - tileCentre.x) > .001f ||
                Mathf.Abs(map.transform.position.z - tileCentre.z) > .001f || !MeasureTile(map, start, r))
                return TerrainFailure("incomplete footprint tile or unsupported collision mesh");
        }
        if (double.IsInfinity(requiredHeight) || double.IsNaN(requiredHeight) ||
            requiredHeight + .002 < centreGround + r) return TerrainFailure("footprint disagrees with centre ground");
        terrainProved = true;
        terrainReason = "measured footprint";
        return true;
    }

    private bool TerrainFailure(string reason)
    {
        terrainReason = reason;
        return false;
    }

    private static bool TerrainGrid(Heightmap map) => map.isActiveAndEnabled &&
        map.m_width > 0 && map.m_width <= 128 && map.m_scale >= .25f && map.m_scale <= 8 &&
        map.transform.rotation == Quaternion.identity && (map.transform.lossyScale - Vector3.one).sqrMagnitude < 1e-10f;

    private bool MeasureTile(Heightmap map, Vector3 start, float r)
    {
        MeshCollider collider = map.GetComponent<MeshCollider>();
        MeshFilter filter = map.GetComponent<MeshFilter>();
        if (!collider || !collider.enabled || !filter || !filter.sharedMesh || !collider.sharedMesh ||
            nativeTerrainMesh == null || (Mesh?)nativeTerrainMesh.GetValue(map) != collider.sharedMesh) return false;
        Mesh mesh = collider.sharedMesh;
        int width = map.m_width, stride = width + 1;
        if (!mesh.isReadable || mesh.vertexCount != stride * stride || mesh.subMeshCount != 1 ||
            mesh.GetTopology(0) != MeshTopology.Triangles || mesh.GetIndexCount(0) != width * width * 6 ||
            terrainTiles == footprintTiles.Length) return false;

        mesh.GetVertices(terrainVertices);
        mesh.GetTriangles(terrainIndices, 0);
        Vector3 centre = map.transform.position;
        float half = width * map.m_scale * .5f;
        float lowX = centre.x - half, lowZ = centre.z - half;
        int minX = Mathf.Clamp(Mathf.FloorToInt((start.x - r - lowX) / map.m_scale), 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.FloorToInt((start.x + r - lowX) / map.m_scale), 0, width - 1);
        int minZ = Mathf.Clamp(Mathf.FloorToInt((start.z - r - lowZ) / map.m_scale), 0, width - 1);
        int maxZ = Mathf.Clamp(Mathf.FloorToInt((start.z + r - lowZ) / map.m_scale), 0, width - 1);
        var camera = new AutonomyPosition(start.x, start.y, start.z);
        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            int a = z * stride + x, b = a + 1, c = a + stride, d = c + 1;
            int offset = (z * width + x) * 6;
            if (terrainIndices[offset] != a || terrainIndices[offset+1] != c || terrainIndices[offset+2] != b ||
                terrainIndices[offset+3] != b || terrainIndices[offset+4] != c || terrainIndices[offset+5] != d)
                return false;
            if (!ReadVertex(a, x, z, out var v00) || !ReadVertex(b, x + 1, z, out var v10) ||
                !ReadVertex(c, x, z + 1, out var v01) || !ReadVertex(d, x + 1, z + 1, out var v11) ||
                !JourneyTerrainContactPolicy.TryRequiredHeight(camera, r, v00, v01, v10, out double first) ||
                !JourneyTerrainContactPolicy.TryRequiredHeight(camera, r, v10, v01, v11, out double second)) return false;
            requiredHeight = Math.Max(requiredHeight, Math.Max(first, second));
        }
        footprintTiles[terrainTiles++] = collider;
        return true;

        bool ReadVertex(int index, int x, int z, out AutonomyPosition result)
        {
            Vector3 vertex = terrainVertices[index] + centre;
            result = default;
            if (!WithinCoreBounds(vertex) || Mathf.Abs(vertex.x - (lowX + x * map.m_scale)) > .002f ||
                Mathf.Abs(vertex.z - (lowZ + z * map.m_scale)) > .002f) return false;
            result = new AutonomyPosition(vertex.x, vertex.y, vertex.z);
            return true;
        }
    }
}
