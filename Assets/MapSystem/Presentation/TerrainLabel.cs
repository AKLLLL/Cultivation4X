using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 地图上的 TextMesh 标签：设置文字与颜色，每帧朝向主相机。
    /// 供 RegionNameRenderer（区域名）与 MapIconRenderer（地点名）复用。
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public sealed class TerrainLabel : MonoBehaviour
    {
        private TextMesh textMesh;
        private Material ownedMaterial;
        private bool flatOnPlane;
        private bool yAxisBillboard;
        private bool groundFixed;

        public void Set(string text, Color color)
        {
            EnsureMesh();
            textMesh.text = text ?? string.Empty;
            textMesh.color = color;
        }

        /// <summary>平铺在地图平面上（区域名使用）；否则每帧朝向相机。</summary>
        public void SetFlat(bool flat)
        {
            flatOnPlane = flat;
            yAxisBillboard = false;
            groundFixed = false;
        }

        /// <summary>只绕 Y 轴朝向相机，不上下倾斜（世界空间区域名推荐）。</summary>
        public void SetYAxisBillboard(bool enabled)
        {
            yAxisBillboard = enabled;
            flatOnPlane = false;
            groundFixed = false;
        }

        /// <summary>完全固定贴在地面上，像印在地表的名字，不随相机旋转。</summary>
        public void SetGroundFixed(bool enabled)
        {
            groundFixed = enabled;
            flatOnPlane = false;
            yAxisBillboard = false;
        }

        public void SetCharacterSize(float size)
        {
            EnsureMesh();
            textMesh.characterSize = Mathf.Max(0.01f, size);
        }

        public bool IsFlat => flatOnPlane;
        public bool IsYAxisBillboard => yAxisBillboard;
        public bool IsGroundFixed => groundFixed;

        private void EnsureMesh()
        {
            if (textMesh != null) return;
            textMesh = GetComponent<TextMesh>();
            if (textMesh == null) textMesh = gameObject.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) textMesh.font = font;
            textMesh.fontSize = 64;
            textMesh.characterSize = 0.12f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            // 文字材质队列高于地形颜色层(3100)，避免被半透明顶面覆盖。
            if (font != null && ownedMaterial == null)
            {
                ownedMaterial = new Material(font.material) { renderQueue = 3200 };
                textMesh.GetComponent<MeshRenderer>().sharedMaterial = ownedMaterial;
            }
        }

        private void Update()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            if (groundFixed)
            {
                // 完全锁死，像印在地表上的字。
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                transform.localScale = Vector3.one;
                return;
            }
            if (yAxisBillboard)
            {
                Vector3 lookPosition = camera.transform.position - transform.position;
                lookPosition.y = 0f;
                transform.localScale = Vector3.one;
                if (lookPosition.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(-lookPosition);
                return;
            }
            if (flatOnPlane)
            {
                transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                // Y 轴取反修正平铺文字的方向：从 65° 俯视角看为正立不镜像。
                transform.localScale = new Vector3(1f, -1f, 1f);
                return;
            }
            Vector3 direction = transform.position - camera.transform.position;
            if (direction.sqrMagnitude < 0.0001f) return;
            // 用相机的 up 作为上方向，90° 完全俯视时标签仍然可读（正上方不退化）。
            transform.rotation = Quaternion.LookRotation(direction, camera.transform.up);
        }

        private void OnDestroy()
        {
            if (ownedMaterial != null)
            {
                if (Application.isPlaying) Destroy(ownedMaterial);
                else DestroyImmediate(ownedMaterial);
                ownedMaterial = null;
            }
        }
    }
}
