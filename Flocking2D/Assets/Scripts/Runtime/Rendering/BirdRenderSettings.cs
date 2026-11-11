using UnityEngine;

public class BirdRenderSettings : MonoBehaviour
{
    public Mesh mesh;
    public Material material;

    public Color slowColor = new Color(1f, 0.05f, 0.02f, 1f);
    public Color fastColor = new Color(1f, 1f, 0f, 1f);
    [Min(0.01f)] public float colorSpeedRange = 0.5f;
}
