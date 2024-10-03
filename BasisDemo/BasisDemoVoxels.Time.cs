using UnityEngine;

public partial class BasisDemoVoxels
{
    [Header("Time")]
    public float timeSpeed = 1f;
    [Range(0f, 1f)]
    public float minAmbientLight = 0.75f;
    private float timeRotation;
    private float lastSentTime;

    public void UpdateTimeCycle()
    {
        timeRotation += Time.deltaTime * timeSpeed;
        timeRotation %= 360f;
        if (IsOwner)
        {
            if (Mathf.Abs(timeRotation - lastSentTime) > 5f)
            {
                SendTime(timeRotation);
                lastSentTime = timeRotation;
            }
        }
        sun.transform.eulerAngles = new Vector3(timeRotation, 30f, 0f);
        float amount = Mathf.Max(0f, 0.5f - Vector3.Dot(Vector3.up, sun.transform.forward));
        sun.intensity = amount;
        RenderSettings.fogColor = Color.gray * amount;
        amount = Mathf.Max(minAmbientLight, amount);
        RenderSettings.ambientIntensity = amount > minAmbientLight ? 1f : minAmbientLight;
    }
}