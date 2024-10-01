using UnityEngine;

public partial class BasisDemoVoxels
{
    [Header("Time")]
    public float timeSpeed = 1f;
    private float timeRotation;
    private float lastSentTime;

    public void UpdateTimeCycle()
    {
        timeRotation += Time.deltaTime * timeSpeed;
        timeRotation %= 360f;
        if (IsOwner)
        {
            if (Mathf.Abs(timeRotation - lastSentTime) > 1f)
            {
                SendTime(timeRotation);
                lastSentTime = timeRotation;
            }
        }
        sun.transform.eulerAngles = new Vector3(timeRotation, 30f, 0f);
        float amount = Mathf.Max(0f, 0.5f - Vector3.Dot(Vector3.up, sun.transform.forward));
        RenderSettings.ambientIntensity = amount;
        RenderSettings.fogColor = Color.gray * amount;
        sun.intensity = amount;
    }
}