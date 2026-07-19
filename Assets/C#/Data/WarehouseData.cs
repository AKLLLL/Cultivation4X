using System;
using System.Collections.Generic;
/// <summary>
/// 仓库静态数据
/// </summary>
[Serializable]
public class WarehouseData
{

    /// <summary>
    /// 仓库里面所有物品
    /// </summary>
    public List<ItemStack> items = new List<ItemStack>();

}