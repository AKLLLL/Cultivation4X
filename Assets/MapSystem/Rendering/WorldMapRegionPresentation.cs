using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    public enum WorldMapZoomLevel { Far = 0, Mid = 1, Near = 2 }

    public sealed class WorldMapRegionLabelCandidate
    {
        public string regionId;
        public int cellIndex = -1;
        public int displayPriority;
        public bool isKnown;
        public bool isSelected;
        public bool isInViewport;
        public bool isInSafeArea = true;
        public float screenX;
        public float screenY;
        public float width;
        public float height;
    }

    public sealed class WorldMapDetailLabelCandidate
    {
        public int cellIndex = -1;
        public InfluenceLevel influenceLevel;
        public bool isSelected;
        public bool isInViewport;
        public bool isInSafeArea = true;
        public float screenX;
        public float screenY;
        public float width;
        public float height;
    }

    public struct WorldMapLabelSafeArea
    {
        public float left;
        public float right;
        public float bottom;
        public float top;

        public bool Contains(float centerX, float centerY, float width, float height) =>
            centerX - width * 0.5f >= left && centerX + width * 0.5f <= right &&
            centerY - height * 0.5f >= bottom && centerY + height * 0.5f <= top;
    }

    public static class WorldMapRegionPresentationPolicy
    {
        public const int FarRegionLabelLimit = 48;
        public const int MidRegionLabelLimit = 24;
        public const int NearDetailLabelLimit = 24;
        public const bool DebugOverlayEnabledByDefault = false;

        public static WorldMapZoomLevel GetZoomLevel(float projectedHexDiameter)
        {
            if (projectedHexDiameter < 18f) return WorldMapZoomLevel.Far;
            if (projectedHexDiameter < 44f) return WorldMapZoomLevel.Mid;
            return WorldMapZoomLevel.Near;
        }

        public static bool ShowRegionBoundary(WorldMapZoomLevel zoom, bool cellKnown, bool neighborKnown) =>
            zoom == WorldMapZoomLevel.Far || cellKnown || neighborKnown;

        public static bool ShowOrdinaryHint(WorldMapZoomLevel zoom) => zoom != WorldMapZoomLevel.Far;

        public static bool ShowMarker(WorldMapMarkerKind kind, WorldMapZoomLevel zoom)
        {
            if (zoom != WorldMapZoomLevel.Far) return true;
            return kind != WorldMapMarkerKind.ContentHint &&
                   kind != WorldMapMarkerKind.EnvironmentHint &&
                   kind != WorldMapMarkerKind.EnvironmentMoisture &&
                   kind != WorldMapMarkerKind.EnvironmentMineralVein &&
                   kind != WorldMapMarkerKind.EnvironmentBeastTracks &&
                   kind != WorldMapMarkerKind.EnvironmentRuinedWalls &&
                   kind != WorldMapMarkerKind.EnvironmentSettlementSigns &&
                   kind != WorldMapMarkerKind.EnvironmentCaveSigns;
        }

        public static bool ShowNearDetail(int effectiveSeed, int cellIndex, KnowledgeState knowledge,
            InfluenceLevel influenceLevel)
        {
            if (knowledge != KnowledgeState.Known) return false;
            if (influenceLevel == InfluenceLevel.Core) return true;
            if (influenceLevel != InfluenceLevel.Influence) return false;
            return (StableUnsigned(effectiveSeed, "near-region-detail-" + cellIndex) & 1u) == 0u;
        }

        public static List<WorldMapRegionLabelCandidate> SelectRegionLabels(
            IEnumerable<WorldMapRegionLabelCandidate> candidates, WorldMapZoomLevel zoom)
        {
            if (zoom == WorldMapZoomLevel.Near) return new List<WorldMapRegionLabelCandidate>();
            int limit = zoom == WorldMapZoomLevel.Far ? FarRegionLabelLimit : MidRegionLabelLimit;
            var selected = new List<WorldMapRegionLabelCandidate>();
            foreach (WorldMapRegionLabelCandidate candidate in (candidates ??
                         Enumerable.Empty<WorldMapRegionLabelCandidate>())
                     .Where(item => item != null && item.isInViewport && item.isInSafeArea &&
                                    !string.IsNullOrEmpty(item.regionId))
                     .Where(item => zoom == WorldMapZoomLevel.Far || item.isSelected || item.isKnown || item.displayPriority >= 55)
                     .OrderByDescending(item => item.isSelected)
                     .ThenByDescending(item => item.isKnown)
                     .ThenByDescending(item => item.displayPriority)
                     .ThenBy(item => item.regionId, StringComparer.Ordinal))
            {
                if (selected.Count >= limit) break;
                if (selected.Any(existing => Overlaps(existing, candidate))) continue;
                selected.Add(candidate);
            }
            return selected;
        }

        public static WorldMapLabelSafeArea CreateGameplaySafeArea(float screenWidth, float screenHeight)
        {
            float width = Math.Max(1f, screenWidth);
            float height = Math.Max(1f, screenHeight);
            return new WorldMapLabelSafeArea
            {
                left = 12f,
                right = width * 0.70f - 12f,
                bottom = Math.Max(72f, height * 0.10f),
                top = height - Math.Max(88f, height * 0.12f)
            };
        }

        public static Vector2 LabelScreenSize(Vector2 preferredLocalSize, float canvasScale,
            float horizontalPadding, float verticalPadding, float minWidth, float minHeight)
        {
            float safeScale = canvasScale > 0f ? canvasScale : 1f;
            return new Vector2(
                Mathf.Max(minWidth, (preferredLocalSize.x + horizontalPadding) * safeScale),
                Mathf.Max(minHeight, (preferredLocalSize.y + verticalPadding) * safeScale));
        }

        public static List<WorldMapDetailLabelCandidate> SelectNearDetailLabels(
            IEnumerable<WorldMapDetailLabelCandidate> candidates, int effectiveSeed,
            int maximum = NearDetailLabelLimit)
        {
            var selected = new List<WorldMapDetailLabelCandidate>();
            foreach (WorldMapDetailLabelCandidate candidate in (candidates ??
                         Enumerable.Empty<WorldMapDetailLabelCandidate>())
                     .Where(item => item != null && item.isInViewport && item.isInSafeArea &&
                                    item.cellIndex >= 0)
                     .OrderByDescending(item => item.isSelected)
                     .ThenByDescending(item => item.influenceLevel)
                     .ThenBy(item => StableUnsigned(effectiveSeed, "near-label-" + item.cellIndex))
                     .ThenBy(item => item.cellIndex))
            {
                if (selected.Count >= Math.Max(0, maximum)) break;
                if (selected.Any(existing => Overlaps(existing, candidate))) continue;
                selected.Add(candidate);
            }
            return selected;
        }

        public static List<int> SelectNearDetailCells(IEnumerable<int> candidates, int selectedCellIndex,
            int maximum = NearDetailLabelLimit)
        {
            return (candidates ?? Enumerable.Empty<int>()).Distinct()
                .OrderByDescending(index => index == selectedCellIndex)
                .ThenBy(index => index).Take(Math.Max(0, maximum)).ToList();
        }

        private static bool Overlaps(WorldMapRegionLabelCandidate left, WorldMapRegionLabelCandidate right) =>
            Math.Abs(left.screenX - right.screenX) * 2f < left.width + right.width &&
            Math.Abs(left.screenY - right.screenY) * 2f < left.height + right.height;

        private static bool Overlaps(WorldMapDetailLabelCandidate left, WorldMapDetailLabelCandidate right) =>
            Math.Abs(left.screenX - right.screenX) * 2f < left.width + right.width &&
            Math.Abs(left.screenY - right.screenY) * 2f < left.height + right.height;

        private static uint StableUnsigned(int seed, string label) =>
            unchecked((uint)SeedDerivation.Derive(seed, label));
    }
}
