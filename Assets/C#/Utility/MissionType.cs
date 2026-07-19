/// <summary>
/// 任务类型
/// 用于任务分类、筛选、AI判断
/// </summary>
public enum MissionType
{
    // 弟子外派任务
    Disciple,

    // 宗门建设管理
    Sect,

    //资源采集任务
    Resource,
    // 炼丹、炼器、制造
    Production,

   // 探索秘境、遗迹、地图
    Exploration,

    // 战斗讨伐
    Combat,

    // 闭关修炼，后面融入为NPC行为系统
    Cultivation,

    //外交任务，后面融入为外交系统
    Diplomacy,

    // 世界随机事件，后面融入为随机事件系统
    WorldEvent

}