using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 世界地点标记渲染器：把 WorldLocation 显示为世界空间文字标记。
    /// 与 MapIconRenderer 分离，后续新增地点类型只需扩展 MarkerSymbol。
    /// </summary>
    public static class LocationMarkerRenderer
    {
        public static GameObject CreateRoot(Vector2 center, float top,
            LocationType type, string name)
        {
            GameObject root = new GameObject("LocationMarker_" + type);
            root.transform.localPosition = new Vector3(center.x, top, center.y);

            TerrainLabel marker = root.AddComponent<TerrainLabel>();
            marker.Set(MarkerSymbol(type), MarkerColor(type));
            marker.SetCharacterSize(0.45f);
            marker.SetYAxisBillboard(true);

            GameObject nameObject = new GameObject("LocationMarkerName", typeof(TerrainLabel));
            nameObject.transform.SetParent(root.transform, false);
            nameObject.transform.localPosition = new Vector3(0f, -0.42f, 0f);
            TerrainLabel nameLabel = nameObject.GetComponent<TerrainLabel>();
            nameLabel.Set(name ?? string.Empty, new Color(1f, 1f, 1f, 0.92f));
            nameLabel.SetCharacterSize(0.13f);
            nameLabel.SetYAxisBillboard(true);
            return root;
        }

        public static string MarkerSymbol(LocationType type)
        {
            switch (type)
            {
                case LocationType.Village: return "村";
                case LocationType.Sect: return "宗";
                case LocationType.ResourceNode: return "矿";
                case LocationType.Ruins: return "墟";
                case LocationType.MonsterNest: return "危";
                default: return "?";
            }
        }

        public static Color MarkerColor(LocationType type)
        {
            switch (type)
            {
                case LocationType.Village: return new Color(0.55f, 0.85f, 0.95f, 1f);
                case LocationType.Sect: return new Color(0.95f, 0.75f, 0.35f, 1f);
                case LocationType.ResourceNode: return new Color(0.95f, 0.85f, 0.45f, 1f);
                case LocationType.Ruins: return new Color(0.75f, 0.70f, 0.90f, 1f);
                case LocationType.MonsterNest: return new Color(0.95f, 0.45f, 0.30f, 1f);
                default: return Color.white;
            }
        }
    }
}
