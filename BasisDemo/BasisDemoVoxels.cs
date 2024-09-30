using System.Collections.Generic;
using System.IO;
using System.Linq;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedPlayer;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class BasisDemoVoxels : VoxelWorld
{
    private BasisNetworkedPlayer LocalNetworkPlayer;
    [Header("Basis Demo")]
    public List<VoxelType> types = new List<VoxelType>();

    public Transform highlighter;
    public GameObject breakBlockSound;
    public GameObject inventoryPrefab;

    private Transform[] highlighters;

    public ushort OwnerId = 0;
    public bool IsOwner = false;

    public float interactDistance = 5f;
    public float networkPlayerBlockDist = 1.1f;
    public int renderDistance = 5;
    public LayerMask layers;
    public LayerMask playerLayers;
    public byte placeBlockId = 1;

    public List<string> htmlVoxelColors = new List<string>();

    public bool genOnStart = false;

    private void Awake()
    {
        materials.Clear();
        materials.AddRange(types.Select(x => x.material));
        htmlVoxelColors.Clear();
        htmlVoxelColors.AddRange(types.Select(x => x.htmlColor));
        InitNetworking();
    }

    private void Start()
    {
        inventoryAction.Enable();
        if (!genOnStart)
            seed = Random.Range(0, int.MaxValue);
        if (BasisLocalPlayer.Instance == null)
        {
            BasisLocalPlayer.OnLocalPlayerCreatedAndReady += InitLocalPlayer;
        }
        else
            InitLocalPlayer();
        if (genOnStart)
            _ = GenerateMap(true);
    }

    private void InitLocalPlayer()
    {
        BasisLocalPlayer.Instance.LocalBoneDriver.OnPostSimulate += OnPostSimulate;
        BasisDeviceManagement.Instance.AllInputDevices.OnListChanged += FindTrackerRoles;
        // BasisDeviceManagement.Instance.AllInputDevices.OnListItemRemoved += ResetIfNeeded; // TODO
        FindTrackerRoles();
        BasisLocalPlayer.OnLocalPlayerCreatedAndReady -= InitLocalPlayer;
    }

    private void OnDestroy()
    {
        DeInitNetworking();
        BasisLocalPlayer.Instance.LocalBoneDriver.OnPostSimulate -= OnPostSimulate;
        BasisDeviceManagement.Instance.AllInputDevices.OnListChanged -= FindTrackerRoles;
    }

    [ContextMenu(nameof(SaveWorld))]
    public void SaveWorld()
    {
        TxtVoxelFile voxelFile = new TxtVoxelFile(htmlVoxelColors.ToArray());
        File.WriteAllText("world.txt", voxelFile.Write(Vector3Int.one * Chunk.SIZE * -renderDistance, Vector3Int.one * Chunk.SIZE * renderDistance, this));
    }

    [ContextMenu(nameof(TestCrash))]
    public void TestCrash()
    {
        UpdateChunk(Vector3.zero);
        UpdateChunk(Vector3.zero);
    }

#region Inputs

    [Header("Inputs")]
    public InputAction inventoryAction;

    private BasisInput centerEye;
    private BasisInput leftHand;
    private BasisInput rightHand;
    private bool lastTriggerLeftMouse = false;
    private bool lastTriggerRightMouse = false;
    private bool lastInventoryButton = false;

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

    private void OnPostSimulate()
    {
        if (highlighters == null)
            return;
        if (BasisUIManagement.Instance.basisUIBases.Count != 0)
            return;
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            if (BasisLocalInputActions.Instance != null && centerEye != null)
            {
                Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                PlaceHighlighter(dir, highlighters[0]);
                bool leftMouse = BasisLocalInputActions.Instance.LeftMousePressed.action.ReadValue<float>() >= 0.5f;
                bool rightMouse = BasisLocalInputActions.Instance.RightMousePressed.action.ReadValue<float>() >= 0.5f;
                bool inventoryBtn = inventoryAction.ReadValue<float>() >= 0.5f;
                if (leftMouse && !lastTriggerLeftMouse)
                {
                    TryDestroyBlock(dir);
                }
                if (rightMouse && !lastTriggerRightMouse)
                {
                    TryPlaceBlock(dir, placeBlockId);
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
                if (centerEye.InputState.Trigger >= 0.5f && centerEye.LastState.Trigger < 0.5f)
                {
                    TryDestroyBlock(dir);
                }
            }
            if (leftHand != null)
            {
                Ray dir = new Ray(leftHand.transform.position, leftHand.transform.forward);
                PlaceHighlighter(dir, highlighters[1]);
                if (leftHand.InputState.Trigger >= 0.5f && leftHand.LastState.Trigger < 0.5f)
                {
                    TryDestroyBlock(dir);
                }
            }
            if (rightHand != null)
            {
                Ray dir = new Ray(rightHand.transform.position, rightHand.transform.forward);
                PlaceHighlighter(dir, highlighters[2]);
                if (rightHand.InputState.Trigger >= 0.5f && rightHand.LastState.Trigger < 0.5f)
                {
                    TryPlaceBlock(dir, placeBlockId);
                }
            }
        }
    }

#endregion

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
                inv.FillBlocks(this);
            }
            else
            {
                Destroy(obj);
            }
        }
    }

    public void PlaceHighlighter(Ray ray, Transform highlight)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) && hit.transform.TryGetComponent(out VoxelMesh _))
        {
            Vector3 block = hit.point - hit.normal * 0.5f;
            Vector3Int pos = GetVoxelPosition(block);
            highlight.position = pos + Vector3.one * 0.5f;
            highlight.gameObject.SetActive(true);
            if (highlight.TryGetComponent(out LineRenderer renderer))
            {
                renderer.SetPositions(new Vector3[] { ray.origin, hit.point });
                renderer.enabled = !BasisDeviceManagement.IsUserInDesktop();
            }
        }
        else
        {
            highlight.gameObject.SetActive(false);
        }
    }

    public void PlayBlockSoundAt(Vector3Int pos)
    {
        GameObject go = Instantiate(breakBlockSound, pos + Vector3.one * 0.5f, Quaternion.identity, transform);
        go.SetActive(true);
        if (go.TryGetComponent(out TempAudio audio))
        {
            audio.Play();
        }
        else
        {
            Destroy(go);
        }
    }

    public bool IsEntityBlocking(Vector3Int pos)
    {
        foreach (var plr in BasisNetworkManagement.Players)
        {
            if (plr.Value.Player is BasisRemotePlayer remote)
            {
                if (remote.RemoteBoneDriver.FindBone(out BasisBoneControl ctrl, BasisBoneTrackedRole.Hips))
                {
                    Vector3Int playerPos = GetVoxelPosition(ctrl.BoneTransform.position);
                    if ((playerPos - pos).sqrMagnitude <= networkPlayerBlockDist * networkPlayerBlockDist)
                    {
                        return true;
                    }
                }
            }
        }
        return Physics.CheckBox(pos + Vector3.one * 0.5f, Vector3.one * 0.45f, Quaternion.identity, playerLayers);
    }

    public void TryDestroyBlock(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) && hit.transform.TryGetComponent(out VoxelMesh _))
        {
            Vector3 block = hit.point - hit.normal * 0.5f;
            if ((int)block.y == 0)
                return;
            Voxel vox = GetVoxel(block);
            Vector3Int pos = GetVoxelPosition(block);
            if (vox != null)
            {
                vox.Id = 0;
                PlayBlockSoundAt(pos);
                SendVoxel(pos, 0);
            }
            UpdateChunks(block);
        }
    }

    public void TryPlaceBlock(Ray ray, byte id)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) && hit.transform.TryGetComponent(out VoxelMesh _))
        {
            Vector3 block = hit.point + hit.normal * 0.5f;
            Voxel vox = GetVoxel(block);
            Vector3Int pos = GetVoxelPosition(block);
            if (IsEntityBlocking(pos))
                return;
            if (vox != null)
            {
                vox.Id = id;
                PlayBlockSoundAt(pos);
                SendVoxel(pos, id);
            }
            UpdateChunks(block);
        }
    }
}