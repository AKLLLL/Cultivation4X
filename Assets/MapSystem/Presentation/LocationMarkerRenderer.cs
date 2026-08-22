using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>世界地点标记的纯表现状态：常态只显示符号，选中后显示小型名称。</summary>
    public sealed class WorldLocationMarkerView : MonoBehaviour
    {
        [SerializeField] private TerrainLabel nameLabel;

        public bool IsNameVisible => nameLabel != null && nameLabel.gameObject.activeSelf;

        public void Configure(TerrainLabel label)
        {
            nameLabel = label;
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (nameLabel != null) nameLabel.gameObject.SetActive(selected);
        }
    }

    /// <summary>
    /// 世界地点标记渲染器：常态显示紧凑类型符号，选中时才显示地点名。
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
            marker.SetCharacterSize(0.24f);
            marker.SetYAxisBillboard(true);

            GameObject nameObject = new GameObject("LocationMarkerName", typeof(TerrainLabel));
            nameObject.transform.SetParent(root.transform, false);
            nameObject.transform.localPosition = new Vector3(0f, -0.24f, 0f);
            TerrainLabel nameLabel = nameObject.GetComponent<TerrainLabel>();
            nameLabel.Set(name ?? string.Empty, new Color(0.92f, 0.91f, 0.80f, 0.96f));
            nameLabel.SetCharacterSize(0.08f);
            nameLabel.SetYAxisBillboard(true);
            root.AddComponent<WorldLocationMarkerView>().Configure(nameLabel);
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
