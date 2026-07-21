using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// NPC数据模板。
/// 每个NPC都是一个ScriptableObject资产，方便在编辑器中配置。
/// </summary>
[CreateAssetMenu(
    fileName = "NewNPC",
    menuName = "GameData/NPC",
    order = 1)]
public class NPCData : ScriptableObject
{
    [Header("基础信息")]
    // NPC唯一ID
    public string npcID;
    // NPC名称
    public string npcName;
    public int age = 16;
    public List<string> initialTraits = new List<string>();

    [Header("初始成长")]
    //出生等级
    public int level = 1;
    //出生经验
    public int exp = 0;
    //升级所需经验
    public int expToNextLevel = 100;

    [Header("基础属性")]
    public int attack;
    public int intelligence;
    public int agility;

    [Header("天赋")]
    //悟性
    public int comprehension;
    //根骨
    public int physique;
}
