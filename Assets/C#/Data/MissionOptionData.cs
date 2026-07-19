using System;
using System.Collections.Generic;
/// <summary>
/// 任务节点选项
/// </summary>

[Serializable]
public class MissionOptionData
{
    //显示文字
    public string text;

    /// <summary>
    /// 条件类型
    /// 例如:
    /// Attack
    /// Intelligence
    /// None
    /// </summary>
    public string requirementType;



    /// <summary>
    /// 条件数值
    /// </summary>
    public int requirementValue;



    /// <summary>
    /// 成功后的效果
    /// </summary>
    public List<MissionEffectData> effects;



}