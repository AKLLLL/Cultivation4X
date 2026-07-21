using System.Collections.Generic;

/// <summary>
/// 一次任务最终产生的奖励。
///
/// 注意：
/// 这个类只是一个"奖励包"，
/// 不负责发放奖励，也不负责修改玩家数据。
///
/// Mission 在运行过程中可以不断修改 Reward。
/// 当 Mission 完成时，再把整个 Reward 交给 RewardManager 发放。
/// </summary>
[System.Serializable]
public class Reward
{
    /// <summary>
    /// 金币奖励
    /// </summary>
    public int Gold;

    /// <summary>
    /// NPC经验奖励
    /// </summary>
    public int Exp;

    /// <summary>
    /// 物品奖励列表
    /// 一个任务可以获得多个物品。
    /// </summary>
    public List<ItemReward> Items = new List<ItemReward>();

    /// <summary>
    /// 默认构造函数
    /// </summary>
    public Reward()
    {

    }
}
