using UnityEngine;

/// <summary>
/// NPC成长相关工具
/// 所有经验、升级、金币操作统一放在这里。
/// </summary>
public static class NPCGrow
{
    /// <summary>
    /// 获取当前等级升级所需经验
    /// </summary>
    public static int GetNeedExp(int level)
    {
        // 目前采用简单公式
        return 100 + (level - 1) * 50;
    }

    /// <summary>
    /// 增加经验
    /// 自动处理升级
    /// </summary>
    public static void AddExp(NPCRuntime npc, int exp)
    {
        npc.Exp += exp;

        // 经验足够则连续升级
        while (npc.Exp >= GetNeedExp(npc.Level))
        {
            npc.Exp -= GetNeedExp(npc.Level);

            npc.Level++;

            Debug.Log($"{npc.Data.npcName} 升到了 {npc.Level} 级");
        }
      
    }

    /// <summary>
    /// 增加金币
    /// </summary>
    public static void AddGold(NPCRuntime npc, int gold)
    {
        npc.Gold += gold;
    }
}