using System;
using System.Collections.Generic;

/// <summary>
/// 任务节点静态数据
/// 从Json读取
/// </summary>

[Serializable]
public class MissionNodeData
{

    //节点触发类型
    //例如：
    //Day
    //Combat
    //Random
    public string triggerType;


    //触发数值
    //例如第几天触发
    public int triggerValue;



    //事件标题

    public string title;



    //事件描述

    public string description;



    //玩家选择

    public List<MissionOptionData> options;

}