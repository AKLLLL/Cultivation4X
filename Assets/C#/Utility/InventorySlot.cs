[System.Serializable]
public class InventorySlot
{
    public ItemStack Stack;

    public bool IsEmpty
    {
        get
        {
            return Stack == null || Stack.count <= 0;
        }
    }
}