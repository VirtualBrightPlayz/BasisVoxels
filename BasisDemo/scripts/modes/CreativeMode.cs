using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using UnityEngine.InputSystem;

public class CreativeMode : MonoBehaviour, IGameMode
{
    public bool Enabled { get => enabled; set => enabled = value; }

    public BasisDemoVoxels world;

    [Header("Prefabs")]
    public GameObject inventoryPrefab;
    public Transform highlighter;
    private Transform[] highlighters;

    [Header("Inputs")]
    public InputAction inventoryAction;
    public byte placeBlockId = 1;

    private BasisInput centerEye;
    private BasisInput leftHand;
    private BasisInput rightHand;
    private bool lastTriggerLeftMouse = false;
    private bool lastTriggerRightMouse = false;
    private bool lastInventoryButton = false;

    private void OnEnable()
    {
        inventoryAction.Enable();
        BasisLocalPlayer.Instance.OnPreSimulateBones += OnPreSimulate;
        BasisDeviceManagement.Instance.AllInputDevices.OnListChanged += FindTrackerRoles;
        FindTrackerRoles();
    }

    private void OnDisable()
    {
        inventoryAction.Disable();
        BasisLocalPlayer.Instance.OnPreSimulateBones -= OnPreSimulate;
        BasisDeviceManagement.Instance.AllInputDevices.OnListChanged -= FindTrackerRoles;
        if (highlighters != null)
        {
            for (int i = 0; i < highlighters.Length; i++)
            {
                if (highlighters[i] != null)
                {
                    Destroy(highlighters[i].gameObject);
                }
            }
        }
    }

    public void ToggleInventoryUI()
    {
        if (InventoryUI.Instance != null)
        {
            InventoryUI.Instance.CloseThisMenu();
        }
        else
        {
            GameObject obj = Instantiate(inventoryPrefab);
            if (obj.TryGetComponent(out InventoryUI inv))
            {
                inv.Open();
                inv.FillBlocks(world);
            }
            else
            {
                Destroy(obj);
            }
        }
    }

    private void FindTrackerRoles()
    {
        centerEye = FindTrackerByRole(BasisBoneTrackedRole.CenterEye);
        leftHand = FindTrackerByRole(BasisBoneTrackedRole.LeftHand);
        rightHand = FindTrackerByRole(BasisBoneTrackedRole.RightHand);
        if (highlighters != null)
        {
            for (int i = 0; i < highlighters.Length; i++)
            {
                if (highlighters[i] != null)
                {
                    Destroy(highlighters[i].gameObject);
                }
            }
        }
        highlighters = new Transform[3];
        highlighters[0] = Instantiate(highlighter, transform);
        highlighters[1] = Instantiate(highlighter, transform);
        highlighters[2] = Instantiate(highlighter, transform);
    }

    private BasisInput FindTrackerByRole(BasisBoneTrackedRole TrackedRole)
    {
        int count = BasisDeviceManagement.Instance.AllInputDevices.Count;
        for (int Index = 0; Index < count; Index++)
        {
            BasisInput Input = BasisDeviceManagement.Instance.AllInputDevices[Index];
            if (Input != null)
            {
                if (Input.TryGetRole(out BasisBoneTrackedRole role))
                {
                    if (role == TrackedRole)
                    {
                        return Input;
                    }
                }
                else
                {
                    Debug.LogError("Missing Role " + role);
                }
            }
            else
            {
                Debug.LogError("There was a missing BasisInput at " + Index);
            }
        }
        return null;
    }

    private void OnPreSimulate()
    {
        if (highlighters == null)
            return;
        if (BasisUIManagement.basisUIBases.Count != 0)
            return;
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            if (BasisLocalInputActions.Instance != null && centerEye != null)
            {
                Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                world.PlaceHighlighter(dir, highlighters[0]);
                bool leftMouse = BasisLocalInputActions.Instance.LeftMousePressed.action.ReadValue<float>() >= 0.5f;
                bool rightMouse = BasisLocalInputActions.Instance.RightMousePressed.action.ReadValue<float>() >= 0.5f;
                bool inventoryBtn = inventoryAction.ReadValue<float>() >= 0.5f;
                if (leftMouse && !lastTriggerLeftMouse)
                {
                    world.TryDestroyBlock(dir, out _, out _);
                }
                if (rightMouse && !lastTriggerRightMouse)
                {
                    world.TryPlaceBlock(dir, placeBlockId);
                }
                if (inventoryBtn && !lastInventoryButton)
                {
                    ToggleInventoryUI();
                }
                lastTriggerLeftMouse = leftMouse;
                lastTriggerRightMouse = rightMouse;
                lastInventoryButton = inventoryBtn;
            }
        }
        else
        {
            if (centerEye != null)
            {
                Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                highlighters[0].gameObject.SetActive(false);
                if (centerEye.CurrentInputState.Trigger >= 0.5f && centerEye.LastInputState.Trigger < 0.5f)
                {
                    world.TryDestroyBlock(dir, out _, out _);
                }
            }
            if (leftHand != null)
            {
                Ray dir = new Ray(leftHand.transform.position, leftHand.transform.forward);
                world.PlaceHighlighter(dir, highlighters[1]);
                if (leftHand.CurrentInputState.Trigger >= 0.5f && leftHand.LastInputState.Trigger < 0.5f)
                {
                    world.TryDestroyBlock(dir, out _, out _);
                }
            }
            if (rightHand != null)
            {
                Ray dir = new Ray(rightHand.transform.position, rightHand.transform.forward);
                world.PlaceHighlighter(dir, highlighters[2]);
                if (rightHand.CurrentInputState.Trigger >= 0.5f && rightHand.LastInputState.Trigger < 0.5f)
                {
                    world.TryPlaceBlock(dir, placeBlockId);
                }
                if (rightHand.CurrentInputState.SecondaryButtonGetState && !rightHand.LastInputState.SecondaryButtonGetState)
                {
                    ToggleInventoryUI();
                }
            }
        }
    }
}