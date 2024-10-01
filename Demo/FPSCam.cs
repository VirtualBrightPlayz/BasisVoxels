using UnityEngine;
using UnityEngine.InputSystem;

public class FPSCam : MonoBehaviour
{
    private Vector2 rotation;
    public VoxelWorld world;
    public float sense = 2f;
    private bool breakBlock = false;
    private bool placeBlock = false;
    public byte placeId = 1;
    public float speed = 16f;
    public InputActionReference fire1;
    public InputActionReference fire2;
    public InputActionReference mouse;
    public InputActionReference wasd;
    private Vector2 movement;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        InputSystem.Update();
        rotation += mouse.action.ReadValue<Vector2>() * sense;
        rotation.y = Mathf.Clamp(rotation.y, -89f, 89f);
        movement = wasd.action.ReadValue<Vector2>();
        // if (Input.GetButtonDown("Fire1"))
        if (fire1.action.WasPressedThisFrame())
        {
            breakBlock = true;
        }
        // if (Input.GetButtonDown("Fire2"))
        if (fire2.action.WasPressedThisFrame())
        {
            placeBlock = true;
        }
        // rotation.x += Input.GetAxis("Mouse X") * sense;
        // rotation.y += Input.GetAxis("Mouse Y") * sense;
        // rotation.y = Mathf.Clamp(rotation.y, -89f, 89f);
        transform.localRotation = Quaternion.AngleAxis(rotation.x, Vector3.up) * Quaternion.AngleAxis(rotation.y, Vector3.left);
        // transform.position += transform.rotation * (new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical")) * Time.deltaTime * speed);
        transform.position += transform.rotation * (new Vector3(movement.x, 0f, movement.y) * Time.deltaTime * speed);
    }

    private void FixedUpdate()
    {
        if (breakBlock)
        {
            breakBlock = false;
            Ray ray = new Ray(transform.position, transform.forward * 200f);
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.transform.TryGetComponent(out VoxelMesh _))
            {
                Vector3 block = hit.point - hit.normal * 0.5f;
                Voxel vox = world.GetVoxel(block);
                if (vox != null)
                {
                    vox.Id = 0;
                }
                _ = world.UpdateChunks(VoxelWorld.FloorPosition(block), true);
            }
        }
        if (placeBlock)
        {
            placeBlock = false;
            Ray ray = new Ray(transform.position, transform.forward * 200f);
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.transform.TryGetComponent(out VoxelMesh _))
            {
                Vector3 block = hit.point + hit.normal * 0.5f;
                Voxel vox = world.GetVoxel(block);
                if (vox != null)
                {
                    vox.Id = placeId;
                }
                _ = world.UpdateChunks(VoxelWorld.FloorPosition(block), true);
            }
        }
    }
}
