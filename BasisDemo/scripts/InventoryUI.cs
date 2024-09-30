using Basis.Scripts.UI.UI_Panels;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : BasisUIBase
{
    public Button closeUI;
    public RectTransform panel;
    public InventoryUIBlock blockTemplate;
    public static InventoryUI Instance;

    public void Open()
    {
        BasisUIManagement.Instance.AddUI(this);
        InitalizeEvent();
    }

    public void FillBlocks(BasisDemoVoxels world)
    {
        for (int i = 0; i < world.types.Count; i++)
        {
            if (!world.types[i].showInMenu)
                continue;
            InventoryUIBlock btn = Instantiate(blockTemplate, panel);
            btn.SetBlock(world.types[i]);
            btn.world = world;
        }
    }

    public override void DestroyEvent()
    {
        BasisCursorManagement.LockCursor(nameof(InventoryUI));
    }

    public override void InitalizeEvent()
    {
        Instance = this;
        closeUI.onClick.AddListener(CloseThisMenu);
        BasisCursorManagement.UnlockCursor(nameof(InventoryUI));
    }
}