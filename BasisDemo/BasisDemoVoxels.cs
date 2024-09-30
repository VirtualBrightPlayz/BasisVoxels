using System.Collections.Generic;
using System.IO;
using System.Linq;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Networking.NetworkedPlayer;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

public partial class BasisDemoVoxels : VoxelWorld
{
    private BasisNetworkedPlayer LocalNetworkPlayer;
    [Header("Basis Demo")]
    public List<VoxelType> types = new List<VoxelType>();

    public ushort OwnerId = 0;
    public bool IsOwner = false;

    public float interactDistance = 5f;
    public int renderDistance = 5;
    public LayerMask layers;
    public byte placeBlockId = 1;

    public List<string> htmlVoxelColors = new List<string>();

    public bool genOnStart = false;

    private BasisInput centerEye;
    private BasisInput leftHand;
    private BasisInput rightHand;
    private bool lastTriggerLeftMouse = false;
    private bool lastTriggerRightMouse = false;

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

    private void FindTrackerRoles()
    {
        centerEye = FindTrackerByRole(BasisBoneTrackedRole.CenterEye);
        leftHand = FindTrackerByRole(BasisBoneTrackedRole.LeftHand);
        rightHand = FindTrackerByRole(BasisBoneTrackedRole.RightHand);
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
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            if (BasisLocalInputActions.Instance != null && centerEye != null)
            {
                bool leftMouse = BasisLocalInputActions.Instance.LeftMousePressed.action.ReadValue<float>() >= 0.5f;
                bool rightMouse = BasisLocalInputActions.Instance.RightMousePressed.action.ReadValue<float>() >= 0.5f;
                if (leftMouse && !lastTriggerLeftMouse)
                {
                    Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                    TryDestroyBlock(dir);
                }
                if (rightMouse && !lastTriggerRightMouse)
                {
                    Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                    TryPlaceBlock(dir, placeBlockId);
                }
                lastTriggerLeftMouse = leftMouse;
                lastTriggerRightMouse = rightMouse;
            }
        }
        else
        {
            if (centerEye != null)
            {
                if (centerEye.InputState.Trigger >= 0.5f && centerEye.LastState.Trigger < 0.5f)
                {
                    Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                    TryDestroyBlock(dir);
                }
            }
            if (leftHand != null)
            {
                if (leftHand.InputState.Trigger >= 0.5f && leftHand.LastState.Trigger < 0.5f)
                {
                    Ray dir = new Ray(leftHand.transform.position, leftHand.transform.forward);
                    TryDestroyBlock(dir);
                }
            }
            if (rightHand != null)
            {
                if (rightHand.InputState.Trigger >= 0.5f && rightHand.LastState.Trigger < 0.5f)
                {
                    Ray dir = new Ray(rightHand.transform.position, rightHand.transform.forward);
                    TryPlaceBlock(dir, placeBlockId);
                }
            }
        }
    }

#endregion

    public void TryDestroyBlock(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) && hit.transform.TryGetComponent(out VoxelMesh _))
        {
            Vector3 block = hit.point - hit.normal * 0.5f;
            if ((int)block.y == 0)
                return;
            Voxel vox = GetVoxel(block);
            if (vox != null)
            {
                vox.Id = 0;
                SendVoxel(GetVoxelPosition(block), 0);
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
            if (vox != null)
            {
                vox.Id = id;
                SendVoxel(GetVoxelPosition(block), id);
            }
            UpdateChunks(block);
        }
    }
}