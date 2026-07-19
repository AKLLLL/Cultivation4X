using UnityEngine;

/// <summary>
/// 玩家管理器
/// </summary>
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance;

    /// <summary>
    /// 玩家数据
    /// </summary>
    public PlayerData playerData = new PlayerData();

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// 增加金币
    /// </summary>
    public void AddGold(int gold)
    {
        playerData.gold += gold;
    }
}