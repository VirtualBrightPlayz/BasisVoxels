using UnityEngine;

public class Item : MonoBehaviour
{
    public static Collider[] resultCache = new Collider[128];
    public byte itemAmount;
    public ItemData itemData;

    private void Start()
    {
        itemData.InitWorldItem(this);
    }

    private void FixedUpdate()
    {
        int count = Physics.OverlapBoxNonAlloc(transform.position, Vector3.one * 2f, resultCache);
        for (int i = 0; i < count; i++)
        {
            CheckOverlap(resultCache[i]);
        }
    }

    private void CheckOverlap(Collider other)
    {
        if (other.TryGetComponent(out Item item))
        {
            if (itemData == item.itemData && GetInstanceID() > item.GetInstanceID())
            {
                itemAmount += item.itemAmount;
                item.itemAmount = 0;
                Destroy(item.gameObject);
            }
        }
    }
}
