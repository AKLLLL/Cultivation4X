using UnityEngine;


/// <summary>
/// 奖励管理器
/// </summary>
public class RewardManager : MonoBehaviour
{
    public static RewardManager Instance;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    /// <summary>
    /// 发放任务奖励
    /// </summary>
    /// <param name="npc">执行任务的NPC</param>
    /// <param name="reward">任务最终奖励</param>
    public void GiveReward(NPCRuntime npc, Reward reward)
    {
        if (npc == null)
        {
            Debug.LogWarning("RewardManager：NPC为空。");
            return;
        }

        if (reward == null)
        {
            Debug.LogWarning("RewardManager：Reward为空。");
            return;
        }
        // 金币是宗门共享资源；弟子只获得经验/修为。
        if (reward.Gold > 0)
        {
            PlayerManager.Instance.AddGold(reward.Gold);
        }
        // 发放经验
        if (reward.Exp > 0)
        {
            npc.AddCultivation(reward.Exp);
        }
        // 发放物品
        foreach (ItemReward item in reward.Items)
        {
            Debug.Log($"准备发放物品：{item.itemId} × {item.count}");
            WarehouseManager.Instance.AddItem(
                item.itemId,
                item.count
            );
        }

        int basicMaterialReward = 0;
        foreach (ItemReward item in reward.Items)
            if (item.itemId == FacilityRules.BasicMaterialId) basicMaterialReward += item.count;
        TimeManager.Instance?.RecordPreAdvanceResourceChange(reward.Gold, basicMaterialReward);

        Debug.Log($"奖励发放完成：{npc.Data.npcName}，宗门灵材 +{reward.Gold}，{npc.Data.npcName}修为 +{reward.Exp}");
    }

    public bool CanGiveReward(Reward reward)
    {
        return reward != null && WarehouseManager.Instance != null &&
               WarehouseManager.Instance.CanAddRewards(reward.Items);
    }
}
