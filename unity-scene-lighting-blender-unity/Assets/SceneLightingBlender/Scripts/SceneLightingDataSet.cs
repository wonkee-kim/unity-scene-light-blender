using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(fileName = "SceneLightingDataSet", menuName = "ScriptableObjects/LightmapBlending/SceneLightingDataSet", order = 0)]
public class SceneLightingDataSet : ScriptableObject
{
    [Header("Lightmaps")]
    public Texture2D[] lightmaps;
    // public Texture2D[] shadowMasks;

    [Header("Main Light")]
    public Color mainLightColor = Color.white;
    public float mainLightTemperature = 6500f;
    public float mainLightIntensity = 1.0f;

    [Header("GI")]
    public float skyboxIntensity = 1.0f;
    public Cubemap[] reflectionProbeCubemaps;
    public Cubemap defaultReflectionCubemap;
    public SphericalHarmonicsL2[] bakedLightProbes;
}
