using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 地图地点的实际后果。该类只提供规则和一次性结算，不持有 Unity 场景对象。
    /// </summary>
    public static class WorldMapContentEffects
    {
        public const int SpiritSpringDailyCultivation = 1;
        public const int VillageRelationReward = 15;
        public const int VillageReputationReward = 10;
        public const int RuinTechniqueUnderstandingReward = 5;

        private static int lastDailyAppliedDay = int.MinValue;
        private static WorldMap lastDailyAppliedMap;
        private static readonly HashSet<string> appliedCompletionKeys = new HashSet<string>(StringComparer.Ordinal);

        public static void ResetForTests()
        {
            lastDailyAppliedDay = int.MinValue;
            lastDailyAppliedMap = null;
            appliedCompletionKeys.Clear();
        }

        public static bool IsSiteDeveloped(MapSiteData site) =>
            site != null && site.siteState == MapSiteState.Developed &&
            site.ownerSectId == WorldMapProgressRules.PlayerSectOwnerId;

        public static bool HasDevelopedSite(MapSiteType type)
        {
            return IsSiteDeveloped(WorldMapSession.Progress?.mapSites?.FirstOrDefault(item =>
                item != null && item.siteType == type));
        }

        /// <summary>
        /// 每日效果只在时间推进入口调用一次；同一天重复调用不会重复产出。
        /// </summary>
        public static void ApplyDaily(int currentDay)
        {
            bool springActive = HasDevelopedSite(MapSiteType.SpiritSpring);
            if (!springActive) return;
            // 依赖对象尚未初始化时不推进日标记；稍后同一天补齐对象仍应获得效果。
            if (NPCManager.Instance == null) return;
            if (!ReferenceEquals(lastDailyAppliedMap, WorldMapSession.Current))
            {
                lastDailyAppliedMap = WorldMapSession.Current;
                lastDailyAppliedDay = int.MinValue;
            }
            if (lastDailyAppliedDay == currentDay) return;
            lastDailyAppliedDay = currentDay;

            if (springActive)
            {
                foreach (NPCRuntime npc in NPCManager.Instance?.GetAllNPC() ?? new List<NPCRuntime>())
                {
                    if (npc != null && npc.Character != null && npc.Character.IsAlive && npc.State == NPCState.Idle)
                        npc.AddCultivation(SpiritSpringDailyCultivation);
                }
            }
        }

        /// <summary>
        /// 地图任务奖励成功发放后调用。状态已先切换为终态，因此重复领取无法再次进入。
        /// </summary>
        public static void ApplySiteCompletion(MapSiteData site, MapActionType action, int currentDay)
        {
            if (site == null) return;
            if ((site.siteType == MapSiteType.Village || site.siteType == MapSiteType.Ruin) &&
                PlayerManager.Instance == null) return;
            if (site.siteType == MapSiteType.CaveResidence && PlayerManager.Instance == null) return;
            if (site.siteType == MapSiteType.BeastLair && ExternalThreatRules.GetState() == null) return;
            string mapKey = WorldMapSession.Current == null ? string.Empty :
                WorldMapSession.Current.effectiveSeed.ToString();
            string completionKey = mapKey + ":" + site.siteId + ":" + site.siteType;
            if (!appliedCompletionKeys.Add(completionKey)) return;
            switch (site.siteType)
            {
                case MapSiteType.Village:
                    PlayerManager.Instance?.AddVillageRelation(VillageRelationReward);
                    PlayerManager.Instance?.AddReputation(VillageReputationReward);
                    break;
                case MapSiteType.BeastLair:
                    ExternalThreatRules.ApplyBeastLairClearance(currentDay);
                    break;
                case MapSiteType.Ruin:
                    PlayerManager.Instance?.AddTechniqueUnderstanding(RuinTechniqueUnderstandingReward);
                    break;
            }
        }

        public static string EffectSummary(MapSiteType type)
        {
            switch (type)
            {
                case MapSiteType.SpiritSpring: return "开发后：每日为每名空闲存活弟子提供修为+1";
                case MapSiteType.SpiritMine:
                case MapSiteType.ResourceNode: return "开发后：每30天结算一次资源产出";
                case MapSiteType.Village: return "完成后：村落关系+15，宗门声望+10";
                case MapSiteType.BeastLair: return "清理后：抑制尚未排程的兽潮，或延后现有威胁节点";
                case MapSiteType.Ruin: return "调查后：功法理解+5";
                default: return string.Empty;
            }
        }
    }
}
