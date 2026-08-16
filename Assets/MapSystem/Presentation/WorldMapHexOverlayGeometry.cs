using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 玩法覆盖层共用的六边形网格几何工具。覆盖层只生成 MeshRenderer，
    /// 不添加 MeshCollider，因此不会干扰 TerrainRenderer.TryPickCell 的射线命中。
    /// 渲染器只接收显式的地图/进度数据，为未来 MapSnapshot 预留直接替换入口。
    /// </summary>
    internal static class WorldMapHexOverlayGeometry
    {
        public static Vector2[] Corners(Vector2 center, float radius) =>
            HexGeometry.GetCorners(center, radius);

        public static void AppendHexCap(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            Vector2 center, float radius, float height, Color32 color)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(center.x, height, center.y));
            colors.Add(color);
            foreach (Vector2 corner in Corners(center, radius))
            {
                vertices.Add(new Vector3(corner.x, height, corner.y));
                colors.Add(color);
            }
            for (int corner = 0; corner < 6; corner++)
            {
                triangles.Add(start);
                triangles.Add(start + 1 + corner);
                triangles.Add(start + 1 + (corner + 1) % 6);
            }
        }

        /// <summary>支持每个角独立高度的六边帽，用于贴合连续地表的坡面。</summary>
        public static void AppendHexCap(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            Vector2 center, Vector2[] corners, float centerHeight, float[] cornerHeights, Color32 color)
        {
            if (corners == null || corners.Length != 6 || cornerHeights == null || cornerHeights.Length != 6) return;
            int start = vertices.Count;
            vertices.Add(new Vector3(center.x, centerHeight, center.y));
            colors.Add(color);
            for (int corner = 0; corner < 6; corner++)
            {
                vertices.Add(new Vector3(corners[corner].x, cornerHeights[corner], corners[corner].y));
                colors.Add(color);
            }
            for (int corner = 0; corner < 6; corner++)
            {
                triangles.Add(start);
                triangles.Add(start + 1 + corner);
                triangles.Add(start + 1 + (corner + 1) % 6);
            }
        }

        public static void AppendHexRing(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            Vector2 center, float radius, float height, float width, Color32 color)
        {
            Vector2[] corners = Corners(center, radius);
            for (int corner = 0; corner < 6; corner++)
            {
                Vector2 from = corners[corner];
                Vector2 to = corners[(corner + 1) % 6];
                AppendSegment(vertices, colors, triangles,
                    new Vector3(from.x, height, from.y),
                    new Vector3(to.x, height, to.y),
                    width, color);
            }
        }

        /// <summary>每个角独立高度的六边环，贴合连续地表坡面，避免被地形裁剪。</summary>
        public static void AppendHexRing(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            Vector2[] corners, float[] cornerHeights, float width, Color32 color)
        {
            if (corners == null || corners.Length != 6 ||
                cornerHeights == null || cornerHeights.Length != 6) return;
            for (int corner = 0; corner < 6; corner++)
            {
                Vector2 from = corners[corner];
                Vector2 to = corners[(corner + 1) % 6];
                AppendSegment(vertices, colors, triangles,
                    new Vector3(from.x, cornerHeights[corner], from.y),
                    new Vector3(to.x, cornerHeights[(corner + 1) % 6], to.y),
                    width, color);
            }
        }

        public static void AppendSegment(List<Vector3> vertices, List<Color32> colors, List<int> triangles,
            Vector3 from, Vector3 to, float width, Color32 color)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.0000001f || width <= 0f) return;
            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z).normalized;
            Vector3 side = new Vector3(-horizontal.z, 0f, horizontal.x) * (width * 0.5f);
            int start = vertices.Count;
            vertices.Add(from - side);
            vertices.Add(from + side);
            vertices.Add(to - side);
            vertices.Add(to + side);
            for (int index = 0; index < 4; index++) colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        public static Mesh CreateMesh(string name, List<Vector3> vertices,
            List<Color32> colors, List<int> triangles)
        {
            if (vertices.Count == 0 || triangles.Count == 0) return null;
            Mesh mesh = new Mesh
            {
                name = name,
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject CreateObject(string name, Transform parent, Mesh mesh, Material material)
        {
            GameObject obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            obj.GetComponent<MeshRenderer>().sharedMaterial = material;
            return obj;
        }

        public static Material CreateVertexColorMaterial(string name, bool transparent)
        {
            // 所有地图覆盖层统一使用 ZTest Always 的 Overlay Shader，
            // 避免被连续地形遮挡出现“边段缺失”。
            Shader overlayShader = Shader.Find("Unlit/VertexColorOverlay");
            Shader fallback = transparent
                ? Shader.Find("Unlit/VertexColorTransparent") ?? Shader.Find("Sprites/Default")
                : Shader.Find("Unlit/VertexColor") ?? Shader.Find("Sprites/Default");
            Shader shader = overlayShader != null ? overlayShader : fallback;
            Material material = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            material.name = name;
            material.renderQueue = 4000;
            return material;
        }
    }
}
