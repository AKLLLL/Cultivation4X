using System;
using System.Collections.Generic;
using Newtonsoft.Json;
/// <summary>
/// 仓库静态数据
/// </summary>
[Serializable]
public class WarehouseData
{

    /// <summary>
    /// 仓库里面所有物品
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<ItemStack> items = new List<ItemStack>
    {
        new ItemStack { itemId = FacilityRules.SpiritStoneId, count = 100 },
        new ItemStack { itemId = FacilityRules.BasicMaterialId, count = 5 }
    };

}
