using System;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 3D 地图交互：负责“把屏幕点击还原为 WorldCell 索引”。
    /// 是否允许点击由 WorldMap3DController 根据 UI/阶段传入，本组件不读取
    /// PlayerManager / UIManager / WorldMapSession。
    /// </summary>
    public sealed class WorldMapInteractionController : MonoBehaviour
    {
        private const float ClickDragThresholdSquared = 36f;

        [SerializeField] private TerrainRenderer terrainRenderer;

        private Vector3 pointerDownPosition;
        private bool pointerDown;
        private bool lastAllowClick;

        public event Action<int> CellPicked;
        public int SelectedCellIndex { get; private set; } = -1;

        public void UpdateInput(WorldMap map, bool allowClick)
        {
            if (map?.cells == null || terrainRenderer == null || Camera.main == null)
            {
                pointerDown = false;
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                pointerDown = true;
                lastAllowClick = allowClick;
                pointerDownPosition = Input.mousePosition;
            }

            if (!pointerDown || !Input.GetMouseButtonUp(0)) return;
            pointerDown = false;
            if (!lastAllowClick) return;
            if ((Input.mousePosition - pointerDownPosition).sqrMagnitude > ClickDragThresholdSquared) return;
            if (terrainRenderer.TryPickCell(Camera.main, Input.mousePosition, out int cellIndex))
                SelectCell(cellIndex);
        }

        public void SelectCell(int cellIndex)
        {
            if (SelectedCellIndex == cellIndex) return;
            SelectedCellIndex = cellIndex;
            CellPicked?.Invoke(cellIndex);
        }

        public void ClearSelection()
        {
            SelectedCellIndex = -1;
        }

        public void ResetForMap(WorldMap map)
        {
            ClearSelection();
            pointerDown = false;
        }
    }
}
