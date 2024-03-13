using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
 
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

// using GraphicTools;

namespace SceneLightingBlender
{
    [ExecuteInEditMode]
    public class SceneLightingBlender : MonoBehaviour
    {
        private static readonly int PROP_TARGET_TEX = Shader.PropertyToID("_TargetTex");
        private static readonly int PROP_BLEND = Shader.PropertyToID("_BlendSlider");
        private const string KW_SKYBOX_USE_BLENDER = "_USE_BLENDER";
        private const string KW_SKYBOX_TEX_1 = "_SELECTOR_TEX1";
        private const string KW_SKYBOX_TEX_2 = "_SELECTOR_TEX2";

        [Header("Blend")]
        [SerializeField] private bool _UpdateInEditor = false;
        [SerializeField, Range(0, 1)] private float _blendFactor = 0.0f;
        private float _blendFactorCached = 0.0f;

        [Header("Scene Lighting Data Set")]
        [SerializeField] private SceneLightingDataSet _sceneLightingDataDay;
        [SerializeField] private SceneLightingDataSet _sceneLightingDataNight;

        [Header("Main Light")]
        [SerializeField] private Light _mainLight;

        [Header("Lightmaps")]
        [SerializeField] private Material _lightmapBlitMaterial;
        private Texture2D[] _lightmapBlended2D;
        private RenderTexture[] _lightmapBlendedRT;
        private LightmapData[] _lightmapData;

        [Header("Volume")]
        [SerializeField] private Volume _volumeDay;
        [SerializeField] private Volume _volumeNight;

        [Header("Skybox")]
        [SerializeField] private Material _skyboxMaterial;

        [Header("Reflection Probes")]
        [SerializeField] private ReflectionProbe[] _reflectionProbes;
        private RenderTexture[] _reflectionProbesRT;

        [Header("Environment")]
        private RenderTexture _defaultReflectionRT;

        [Header("Light Probes")]
        private NativeArray<SphericalHarmonicsL2> _lightProbesNativeStart;
        private NativeArray<SphericalHarmonicsL2> _lightProbesNativeEnd;
        private NativeArray<SphericalHarmonicsL2> _lightProbesNativeResult;
        private BlendSphericalHarmonicsL2sJob _lightProbesBlendJob;
        private SphericalHarmonicsL2[] _lightProbesResult; // To avoid CG alloc

        private bool _isInitialized = false;

        #region MONOBEHAVIOUR_METHODS

        private void Update()
        {
#if UNITY_EDITOR
        if (!_UpdateInEditor)
            return;
#endif
            if (_blendFactorCached != _blendFactor)
            {
                _blendFactorCached = _blendFactor;
                if (_blendFactor == 0)
                {
                    UpdateLightmap(_sceneLightingDataNight);
                    Cleanup();
                }
                else if (_blendFactor == 1)
                {
                    UpdateLightmap(_sceneLightingDataDay);
                    Cleanup();
                }
                else
                {
                    BlendSceneLightings(_sceneLightingDataNight, _sceneLightingDataDay, _blendFactor);
                }
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        #endregion


        #region PRIVATE_METHODS

        private void UpdateLightmap(SceneLightingDataSet data)
        {
            UpdateLightmap(data.lightmaps, data.mainLightColor, data.mainLightIntensity, data.mainLightTemperature, data.skyboxIntensity, data.reflectionProbeCubemaps, data.defaultReflectionCubemap, data.bakedLightProbes);
        }

        private void UpdateLightmap(Texture2D[] lightmaps, Color mainLightColor, float mainLightIntensity, float mainLightTemperature, float skyboxIntensity, Texture[] reflectionProbes, Texture defaultReflectionCubemap, SphericalHarmonicsL2[] bakedLightProbes)
        {
            // Apply Lightmap Textures
            for (int i = 0; i < _lightmapData.Length; i++)
            {
                _lightmapData[i].lightmapColor = lightmaps[i];
                // lightmapData[i].shadowMask = sceneData.shadowMasks[i]; // TODO: ShadowMask?
            }
            LightmapSettings.lightmaps = _lightmapData;

            // Blend Main Light
            _mainLight.color = mainLightColor;
            _mainLight.intensity = mainLightIntensity;
            _mainLight.colorTemperature = mainLightTemperature;

            // Skybox
            if (_blendFactor == 0)
            {
                _skyboxMaterial.DisableKeyword(KW_SKYBOX_USE_BLENDER);
                _skyboxMaterial.EnableKeyword(KW_SKYBOX_TEX_1);
                _skyboxMaterial.DisableKeyword(KW_SKYBOX_TEX_2);
            }
            else if (_blendFactor == 1)
            {
                _skyboxMaterial.DisableKeyword(KW_SKYBOX_USE_BLENDER);
                _skyboxMaterial.DisableKeyword(KW_SKYBOX_TEX_1);
                _skyboxMaterial.EnableKeyword(KW_SKYBOX_TEX_2);
            }
            else
            {
                _skyboxMaterial.EnableKeyword(KW_SKYBOX_USE_BLENDER);
                _skyboxMaterial.DisableKeyword(KW_SKYBOX_TEX_1);
                _skyboxMaterial.DisableKeyword(KW_SKYBOX_TEX_2);
            }
            _skyboxMaterial.SetFloat(PROP_BLEND, _blendFactor);

            // Reflection Probes
            for (int i = 0; i < _reflectionProbes.Length; i++)
            {
                _reflectionProbes[i].customBakedTexture = reflectionProbes[i];
            }

            // Light Probes
            LightmapSettings.lightProbes.bakedProbes = bakedLightProbes;
            // LightProbes.TetrahedralizeAsync();

            // Environment
            RenderSettings.ambientIntensity = skyboxIntensity;
            RenderSettings.customReflection = defaultReflectionCubemap;

            // Volume
            _volumeDay.weight = _blendFactor;
            _volumeNight.weight = 1 - _blendFactor;
        }

        private void BlendingSetup(SceneLightingDataSet dataStart, SceneLightingDataSet dataEnd)
        {
            if (_isInitialized && Application.isPlaying)
                return;
            _isInitialized = true;

            // Lightmap blending setup
            int lightmapCount = dataStart.lightmaps.Length;
            int width = dataStart.lightmaps[0].width;
            int height = dataStart.lightmaps[0].height;

            _lightmapBlendedRT = new RenderTexture[lightmapCount];
            _lightmapBlended2D = new Texture2D[lightmapCount];
            for (int i = 0; i < lightmapCount; i++)
            {
                _lightmapBlendedRT[i] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                // _lightmapBlendedRT[i].enableRandomWrite = true;
                // _lightmapBlendedRT[i].Create();
                _lightmapBlended2D[i] = new Texture2D(width, height, TextureFormat.ARGB32, false);
            }

            _lightmapData = LightmapSettings.lightmaps;
            if (_lightmapData.Length != lightmapCount)
            {
                _lightmapData = new LightmapData[lightmapCount];
                for (int i = 0; i < _lightmapData.Length; i++)
                {
                    _lightmapData[i] = new LightmapData();
                }
            }

            // ReflectionProbes blending setup
            int reflectionProbeCount = dataStart.reflectionProbeCubemaps.Length;
            _reflectionProbesRT = new RenderTexture[reflectionProbeCount];
            for (int i = 0; i < reflectionProbeCount; i++)
            {
                int probeWidth = dataStart.reflectionProbeCubemaps[i].width;
                _reflectionProbesRT[i] = CreateCubemapRenderTexture(probeWidth);
            }

            // Default reflection blending setup
            int defaultReflectionWidth = dataStart.defaultReflectionCubemap.width;
            _defaultReflectionRT = CreateCubemapRenderTexture(defaultReflectionWidth);

            // Light probes blending setup
            int lightProbesCount = dataStart.bakedLightProbes.Length;
            _lightProbesNativeStart = NativeArrayUtilities.GetNativeArrays(dataStart.bakedLightProbes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _lightProbesNativeEnd = NativeArrayUtilities.GetNativeArrays(dataEnd.bakedLightProbes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _lightProbesNativeResult = new NativeArray<SphericalHarmonicsL2>(lightProbesCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _lightProbesBlendJob = new BlendSphericalHarmonicsL2sJob(_lightProbesNativeStart, _lightProbesNativeEnd, _lightProbesNativeResult);
            _lightProbesResult = new SphericalHarmonicsL2[lightProbesCount];
        }

        private RenderTexture CreateCubemapRenderTexture(int width, RenderTextureFormat foramt = RenderTextureFormat.Default)
        {
            RenderTexture rt = new RenderTexture(width, width, 0, foramt);
            rt.dimension = UnityEngine.Rendering.TextureDimension.Cube;
            rt.useMipMap = true; // Smoothness is not working without this.
            rt.autoGenerateMips = true;
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.Create();
            return rt;
        }

        private void BlendSceneLightings(SceneLightingDataSet dataStart, SceneLightingDataSet dataEnd, float blend)
        {
            BlendingSetup(dataStart, dataEnd);

            // Blend Lightmaps
            int count = dataStart.lightmaps.Length;
            _lightmapBlitMaterial.SetFloat(PROP_BLEND, blend);
            for (int i = 0; i < count; i++)
            {
                _lightmapBlitMaterial.SetTexture(PROP_TARGET_TEX, dataEnd.lightmaps[i]);
                Graphics.Blit(dataStart.lightmaps[i], _lightmapBlendedRT[i], _lightmapBlitMaterial, 0);
                Graphics.CopyTexture(_lightmapBlendedRT[i], _lightmapBlended2D[i]);
            }

            // Blend ReflectionProbes Reflections
            for (int i = 0; i < dataStart.reflectionProbeCubemaps.Length; i++)
            {
                ReflectionProbe.BlendCubemap(dataStart.reflectionProbeCubemaps[i], dataEnd.reflectionProbeCubemaps[i], blend, _reflectionProbesRT[i]);
            }

            // Blend Default Reflection
            ReflectionProbe.BlendCubemap(dataStart.defaultReflectionCubemap, dataEnd.defaultReflectionCubemap, blend, _defaultReflectionRT);

            // Blend light probes
            SphericalHarmonicsL2[] bakedLightProbes = BlendLightProbes(dataStart.bakedLightProbes, dataEnd.bakedLightProbes, blend);

            UpdateLightmap(
                lightmaps: _lightmapBlended2D,
                mainLightColor: Color.Lerp(dataStart.mainLightColor, dataEnd.mainLightColor, blend),
                mainLightIntensity: Mathf.Lerp(dataStart.mainLightIntensity, dataEnd.mainLightIntensity, blend),
                mainLightTemperature: Mathf.Lerp(dataStart.mainLightTemperature, dataEnd.mainLightTemperature, blend),
                skyboxIntensity: Mathf.Lerp(dataStart.skyboxIntensity, dataEnd.skyboxIntensity, blend),
                reflectionProbes: _reflectionProbesRT,
                defaultReflectionCubemap: _defaultReflectionRT,
                bakedLightProbes: bakedLightProbes
                );
        }

        private SphericalHarmonicsL2[] BlendLightProbes(SphericalHarmonicsL2[] lightProbesStart, SphericalHarmonicsL2[] lightProbesEnd, float blend)
        {
            int count = lightProbesStart.Length;

            _lightProbesBlendJob.blend = blend;
            _lightProbesBlendJob.ScheduleParallel(count, innerloopBatchCount: 16, new JobHandle()).Complete();

            // _lightProbesResult = _lightProbesNativeResult.ToArray(); // This causes massive GC allocation. 0.1kb per probe.
            // https://answers.unity.com/questions/1819160/cast-vector3-to-float3.html?sort=votes
            NativeArrayUtilities.GetArrayFromNativeArray(dst: _lightProbesResult, src: _lightProbesNativeResult);
            return _lightProbesResult;
        }

        [BurstCompile]
        public struct BlendSphericalHarmonicsL2sJob : IJobFor
        {
            [ReadOnly] NativeArray<SphericalHarmonicsL2> _lightProbesStart;
            [ReadOnly] NativeArray<SphericalHarmonicsL2> _lightProbesEnd;
            [NativeDisableParallelForRestriction]
            [WriteOnly] NativeArray<SphericalHarmonicsL2> _lightProbesResult;
            public float blend;

            public BlendSphericalHarmonicsL2sJob(NativeArray<SphericalHarmonicsL2> lightProbesStart, NativeArray<SphericalHarmonicsL2> lightProbesEnd, NativeArray<SphericalHarmonicsL2> lightProbesResult)
            {
                this._lightProbesStart = lightProbesStart;
                this._lightProbesEnd = lightProbesEnd;
                this._lightProbesResult = lightProbesResult;
                this.blend = 0f;
            }

            public void Execute(int index)
            {
                SphericalHarmonicsL2 result = new SphericalHarmonicsL2();
                for (int x = 0; x < 3; x++)
                {
                    for (int y = 0; y < 9; y++)
                    {
                        result[x, y] = Mathf.Lerp(_lightProbesStart[index][x, y], _lightProbesEnd[index][x, y], blend);
                    }
                }
                _lightProbesResult[index] = result;
            }
        } // BlendSphericalHarmonicsL2sJob

        private void Cleanup()
        {
            if (_lightmapBlendedRT != null && _lightmapBlendedRT.Length > 0)
            {
                for (int i = 0; i < _lightmapBlendedRT.Length; i++)
                {
                    if (_lightmapBlendedRT[i] != null)
                    {
                        _lightmapBlendedRT[i].Release();
                        _lightmapBlendedRT[i] = null;
                    }
                }
                _lightmapBlendedRT = null;
            }

            if (_reflectionProbesRT != null && _reflectionProbesRT.Length > 0)
            {
                for (int i = 0; i < _reflectionProbesRT.Length; i++)
                {
                    if (_reflectionProbesRT[i] != null)
                    {
                        _reflectionProbesRT[i].Release();
                        _reflectionProbesRT[i] = null;
                    }
                }
                _reflectionProbesRT = null;
            }

            if (_defaultReflectionRT != null)
            {
                _defaultReflectionRT.Release();
                _defaultReflectionRT = null;
            }

            if (_lightProbesNativeStart.IsCreated)
            {
                _lightProbesNativeStart.Dispose();
            }
            if (_lightProbesNativeEnd.IsCreated)
            {
                _lightProbesNativeEnd.Dispose();
            }
            if (_lightProbesNativeResult.IsCreated)
            {
                _lightProbesNativeResult.Dispose();
            }

            _isInitialized = false;
        }

        #endregion


        #region UTILITY

        [ContextMenu(nameof(StoreLightProbeDataToDay))]
        public void StoreLightProbeDataToDay()
        {
            _sceneLightingDataDay.bakedLightProbes = LightmapSettings.lightProbes.bakedProbes;
        }

        [ContextMenu(nameof(StoreLightProbeDataToNight))]
        public void StoreLightProbeDataToNight()
        {
            _sceneLightingDataNight.bakedLightProbes = LightmapSettings.lightProbes.bakedProbes;
        }

        #endregion
    }
}
