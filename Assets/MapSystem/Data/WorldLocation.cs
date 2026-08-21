using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cultivation4X.WorldMap
{
    /// <summary>
    /// 世界地点类型。第一阶段只实现 Village 与 Sect；
    /// 后续矿洞、遗迹、妖兽巢穴等通过新增枚举值接入，不改 UI 结构。
    /// </summary>
    public enum LocationType
    {
        None = 0,
        Village = 1,
        ResourceNode = 2,
        Ruins = 3,
        MonsterNest = 4,
        Sect = 5
    }

    /// <summary>世界地点状态：用于后续解锁/占领/废弃等玩法，第一阶段使用 Active。</summary>
    public enum LocationState
    {
        None = 0,
        Active = 1,
        Inactive = 2,
        Locked = 3
    }

    /// <summary>地点行动类型：由 WorldMap3DController 统一分发到对应系统。</summary>
    public enum LocationActionType
    {
        None = 0,
        Explore = 1,
        ManageLabor = 2,
        ViewStatus = 3,
        ManageSect = 4,
        DevelopResourceNode = 5
    }

    /// <summary>
    /// 地点可执行行为的接口数据。第一阶段只描述显示与可用性，
    /// 实际行为由后续任务/村庄系统接入。
    /// </summary>
    [Serializable]
    public class LocationAction
    {
        public string id;
        public LocationActionType actionType;
        public string displayName;
        public int cost;
        public bool available;
    }

    /// <summary>
    /// 世界地图上的实体。HexCell 只保存 locationId，完整地点数据存于
    /// WorldMap.locations；玩家交互对象是 WorldLocation，而不是 HexCell。
    /// </summary>
    [Serializable]
    public class WorldLocation
    {
        public string id;
        public LocationType type;
        public Vector2Int position;
        public string name;
        public string ownerId;
        public int level;
        public LocationState state;
        public List<LocationAction> availableActions = new List<LocationAction>();
        /// <summary>该地点可提供的既有 Mission 模板 ID。</summary>
        public List<string> availableMissionIds = new List<string>();
        /// <summary>关联的 MapSiteData.siteId；MapSiteData 仍是玩法真实数据，WorldLocation 是地图门面。</summary>
        public string sourceMapSiteId;
    }
}
