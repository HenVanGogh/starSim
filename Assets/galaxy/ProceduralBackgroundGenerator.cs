using UnityEngine;
using UnityEngine.Rendering; // Required for GraphicsSettings
using System.Collections.Generic;

public class ProceduralBackgroundGenerator : MonoBehaviour
{
    // --- Settings Classes (Keep as before) ---
    [System.Serializable]
    public class BackgroundLayerSettings
    {
        public string layerName = "BackgroundLayer";
        public enum TextureType { Stars, Noise }
        public TextureType textureType = TextureType.Stars;

        [Header("Common Settings")]
        public int textureWidth = 1024;
        public int textureHeight = 1024;
        public float zDistance = 1000f;
        public float quadScale = 1500f;
        public float parallaxFactor = 0.1f;

        [Header("Star Settings (if TextureType is Stars)")]
        public float starDensity = 0.001f;
        public int starSize = 1;
        public Color[] starColors = { new Color(1f, 1f, 0.9f), new Color(0.9f, 0.9f, 1f), Color.white };
        public bool useAdditiveBlending = true; // Note: Additive might still fail if particle shader not found

        [Header("Noise Settings (if TextureType is Noise)")]
        public float noiseScale = 10f;
        public float noisePersistence = 0.5f;
        public float noiseLacunarity = 2.0f;
        public int noiseOctaves = 4;
        public float noiseIntensity = 1.0f;
        public Color noiseTint = Color.gray;
        public bool invertNoise = false;
        public bool useAlphaBlending = true;
    }

    [Header("Generation Setup")]
    public List<BackgroundLayerSettings> layers = new List<BackgroundLayerSettings>();
    public bool generateOnStart = true;

    private List<GameObject> generatedLayers = new List<GameObject>();
    private const int BACKGROUND_RENDER_QUEUE = 1000; // Render queue for background elements

    // --- Pipeline Detection ---
    private enum RenderPipelineType { BuiltIn, URP, HDRP, Unknown }
    private RenderPipelineType activePipeline = RenderPipelineType.Unknown;

    void Awake()
    {
        DetectRenderPipeline(); // Detect pipeline early
    }

    void Start()
    {
        if (generateOnStart)
        {
            GenerateBackground();
        }
    }

     /// <summary>
    /// Detects the currently active render pipeline.
    /// </summary>
    void DetectRenderPipeline()
    {
        if (GraphicsSettings.defaultRenderPipeline == null) { activePipeline = RenderPipelineType.BuiltIn; Debug.Log("[ProceduralBackgroundGenerator] Detected Render Pipeline: Built-in"); }
        else if (GraphicsSettings.defaultRenderPipeline.GetType().Name.Contains("UniversalRenderPipelineAsset")) { activePipeline = RenderPipelineType.URP; Debug.Log("[ProceduralBackgroundGenerator] Detected Render Pipeline: URP"); }
        else if (GraphicsSettings.defaultRenderPipeline.GetType().Name.Contains("HDRenderPipelineAsset")) { activePipeline = RenderPipelineType.HDRP; Debug.Log("[ProceduralBackgroundGenerator] Detected Render Pipeline: HDRP"); }
        else { activePipeline = RenderPipelineType.Unknown; Debug.LogWarning("[ProceduralBackgroundGenerator] Could not determine active render pipeline from Graphics Settings."); }
    }

    public void GenerateBackground()
    {
        foreach (GameObject layer in generatedLayers) { if (layer != null) Destroy(layer); }
        generatedLayers.Clear();
        if (layers == null || layers.Count == 0) { Debug.LogWarning("No background layers defined.", this); return; }

        int layerIndex = 0;
        foreach (BackgroundLayerSettings settings in layers)
        {
            layerIndex++;
            GameObject layerObject = CreateLayer(settings, layerIndex);
            if (layerObject != null) { generatedLayers.Add(layerObject); }
        }
        Debug.Log($"[ProceduralBackgroundGenerator] Generated {generatedLayers.Count} procedural background layers.");
    }

    private GameObject CreateLayer(BackgroundLayerSettings settings, int index)
    {
        GameObject layerGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        layerGO.name = $"{settings.layerName}_{index}";
        layerGO.transform.SetParent(transform);
        Destroy(layerGO.GetComponent<MeshCollider>());

        layerGO.transform.localPosition = new Vector3(0, 0, settings.zDistance);
        layerGO.transform.localScale = new Vector3(settings.quadScale, settings.quadScale, 1f);
        layerGO.transform.localRotation = Quaternion.Euler(90, 0, 0); // Vertical orientation

        Texture2D proceduralTexture = null;
        Material layerMaterial = null;
        Shader foundShader = null;
        string shaderNameAttempt = "";
        string fallbackShaderName = "";
        bool needsTransparencySetup = false; // Flag if we ended up using Unlit/Transparent

        // --- Select Shader Based on Pipeline and Settings ---
        if (settings.textureType == BackgroundLayerSettings.TextureType.Stars)
        {
            proceduralTexture = GenerateStarTexture(settings);
            if (settings.useAdditiveBlending) {
                shaderNameAttempt = GetShaderName("ParticleAdditive");
                foundShader = Shader.Find(shaderNameAttempt);
                if (foundShader == null) {
                    Debug.LogWarning($"[ProceduralBackgroundGenerator] Shader '{shaderNameAttempt}' not found. Trying Alpha Blended.", layerGO);
                    shaderNameAttempt = GetShaderName("ParticleAlphaBlended");
                    foundShader = Shader.Find(shaderNameAttempt);
                    needsTransparencySetup = true; // Alpha blended needs setup
                }
                // If Additive was found, it might not need extra setup, but particle shaders often do.
                // Let's assume transparency setup is needed if not opaque.
                 if (foundShader != null) needsTransparencySetup = true;

            } else { // Not additive, try alpha blended / transparent
                 shaderNameAttempt = GetShaderName("ParticleAlphaBlended");
                 foundShader = Shader.Find(shaderNameAttempt);
                 needsTransparencySetup = true; // Alpha blended needs setup
            }
            // Final fallback for stars if others fail
            if (foundShader == null) {
                fallbackShaderName = GetShaderName("UnlitTransparent");
                 Debug.LogWarning($"[ProceduralBackgroundGenerator] Shader '{shaderNameAttempt}' not found. Trying fallback '{fallbackShaderName}'.", layerGO);
                 foundShader = Shader.Find(fallbackShaderName);
                 needsTransparencySetup = true; // Unlit/Transparent needs setup
            }
        }
        else // Noise
        {
            proceduralTexture = GenerateNoiseTexture(settings);
            if (settings.useAlphaBlending) {
                shaderNameAttempt = GetShaderName("ParticleAlphaBlended");
                foundShader = Shader.Find(shaderNameAttempt);
                needsTransparencySetup = true; // Alpha blended needs setup
                if (foundShader == null) {
                    fallbackShaderName = GetShaderName("UnlitTransparent");
                    Debug.LogWarning($"[ProceduralBackgroundGenerator] Shader '{shaderNameAttempt}' not found. Trying fallback '{fallbackShaderName}'.", layerGO);
                    foundShader = Shader.Find(fallbackShaderName);
                    // needsTransparencySetup is already true
                }
            } else { // Opaque Noise
                shaderNameAttempt = GetShaderName("UnlitOpaque");
                foundShader = Shader.Find(shaderNameAttempt);
                needsTransparencySetup = false; // Opaque doesn't need transparency setup
            }
        }

        // --- Last Resort Built-in Fallback ---
        if (foundShader == null)
        {
            fallbackShaderName = "Unlit/Transparent"; // Built-in basic
            Debug.LogWarning($"[ProceduralBackgroundGenerator] Pipeline-specific shaders failed (last attempt: '{shaderNameAttempt}'). Trying built-in fallback '{fallbackShaderName}'. Check URP/HDRP setup and ensure shaders are included.", layerGO);
            foundShader = Shader.Find(fallbackShaderName);
            needsTransparencySetup = true; // Built-in Unlit/Transparent needs setup
        }

        // --- Final Shader Check and Material Creation ---
        if (foundShader == null) {
            Debug.LogError($"[ProceduralBackgroundGenerator] CRITICAL: Failed to find ANY suitable shader. Background layer '{layerGO.name}' cannot be created.", layerGO);
            Destroy(layerGO); return null;
        } else {
             Debug.Log($"[ProceduralBackgroundGenerator] Using shader '{foundShader.name}' for layer {layerGO.name}", layerGO);
             layerMaterial = new Material(foundShader);
        }

        if (proceduralTexture == null) { Destroy(layerGO); return null; }

        // --- Apply Texture and Material Properties ---
        layerMaterial.mainTexture = proceduralTexture;
        layerMaterial.color = Color.white; // Default to white

        // ***** URP Material Property Setup *****
        if (activePipeline == RenderPipelineType.URP && needsTransparencySetup && foundShader.name.Contains("Unlit")) // Only setup for URP Unlit when transparency is needed
        {
             Debug.Log($"[ProceduralBackgroundGenerator] Setting URP Unlit material properties for transparency on {layerGO.name}");
            // Set properties for URP Unlit shader to enable transparency
            // Property names might vary slightly between Unity versions, check shader source if needed
            layerMaterial.SetFloat("_Surface", 1f); // 1 = Transparent Surface Type
            layerMaterial.SetFloat("_Blend", 1f);   // 1 = Premultiply Blend Mode (0=Alpha, 2=Additive, 3=Multiply)
            layerMaterial.SetFloat("_QueueOffset", 0f); // Optional: Adjust render queue offset if needed

            // Enable necessary keywords (may not be strictly required if properties are set, but good practice)
            layerMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            layerMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            layerMaterial.DisableKeyword("_ALPHABLEND_ON"); // Disable other blend modes if enabling premultiply

            // Set render queue specifically for transparent objects within the background range
            layerMaterial.renderQueue = (int)RenderQueue.Transparent - 500 + BACKGROUND_RENDER_QUEUE; // Place it early in transparency queue
        }
        else
        {
             // Use standard background queue for opaque or non-URP Unlit materials
             layerMaterial.renderQueue = BACKGROUND_RENDER_QUEUE;
        }
        // **************************************


        Renderer renderer = layerGO.GetComponent<Renderer>();
        renderer.material = layerMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // --- Parallax ---
        ParallaxLayer parallax = layerGO.AddComponent<ParallaxLayer>();
        parallax.parallaxFactor = settings.parallaxFactor;
        parallax.scrollTexture = false;

        return layerGO;
    }

    // --- GetShaderName (Unchanged) ---
    private string GetShaderName(string shaderType) {
        switch (activePipeline) {
            case RenderPipelineType.URP:
                switch (shaderType) {
                    case "ParticleAdditive": return "Universal Render Pipeline/Particles/Additive";
                    case "ParticleAlphaBlended": return "Universal Render Pipeline/Particles/Alpha Blended";
                    case "UnlitTransparent": return "Universal Render Pipeline/Unlit"; // Base name for transparent setup
                    case "UnlitOpaque": return "Universal Render Pipeline/Unlit";      // Base name for opaque setup
                    default: return "Universal Render Pipeline/Lit";
                }
            // HDRP and Built-in cases remain the same...
            case RenderPipelineType.HDRP:
                 switch (shaderType) {
                    case "ParticleAdditive": return "HDRP/Unlit"; // Guess
                    case "ParticleAlphaBlended": return "HDRP/Unlit"; // Guess
                    case "UnlitTransparent": return "HDRP/Unlit";
                    case "UnlitOpaque": return "HDRP/Unlit";
                    default: return "HDRP/Lit";
                }
            case RenderPipelineType.BuiltIn:
            default:
                switch (shaderType) {
                    case "ParticleAdditive": return "Particles/Additive";
                    case "ParticleAlphaBlended": return "Particles/Alpha Blended";
                    case "UnlitTransparent": return "Unlit/Transparent";
                    case "UnlitOpaque": return "Unlit/Texture";
                    default: return "Standard";
                }
        }
     }

    // --- GenerateStarTexture and GenerateNoiseTexture (Unchanged) ---
    private Texture2D GenerateStarTexture(BackgroundLayerSettings settings) { /* ... same as before ... */
        Texture2D texture = new Texture2D(settings.textureWidth, settings.textureHeight, TextureFormat.ARGB32, false);
        texture.wrapMode = TextureWrapMode.Repeat;
        Color[] pixels = new Color[settings.textureWidth * settings.textureHeight];
        for (int i = 0; i < pixels.Length; i++) { pixels[i] = Color.clear; }
        int numStars = Mathf.FloorToInt(settings.textureWidth * settings.textureHeight * settings.starDensity);
        for (int i = 0; i < numStars; i++) {
            int x = Random.Range(0, settings.textureWidth);
            int y = Random.Range(0, settings.textureHeight);
            Color starColor = settings.starColors[Random.Range(0, settings.starColors.Length)];
            for (int sx = 0; sx < settings.starSize; sx++) {
                for (int sy = 0; sy < settings.starSize; sy++) {
                    int currentX = (x + sx) % settings.textureWidth;
                    int currentY = (y + sy) % settings.textureHeight;
                    int pixelIndex = currentY * settings.textureWidth + currentX;
                    if (settings.useAdditiveBlending) {
                        pixels[pixelIndex] += starColor;
                        pixels[pixelIndex].a = Mathf.Max(pixels[pixelIndex].a, starColor.a);
                    } else {
                        pixels[pixelIndex] = Color.Lerp(pixels[pixelIndex], starColor, starColor.a);
                    }
                }
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
     }
    private Texture2D GenerateNoiseTexture(BackgroundLayerSettings settings) { /* ... same as before ... */
        TextureFormat format = settings.useAlphaBlending ? TextureFormat.ARGB32 : TextureFormat.RGB24;
        Texture2D texture = new Texture2D(settings.textureWidth, settings.textureHeight, format, false);
        texture.wrapMode = TextureWrapMode.Repeat;
        Color[] pixels = new Color[settings.textureWidth * settings.textureHeight];
        float halfWidth = settings.textureWidth / 2f;
        float halfHeight = settings.textureHeight / 2f;
        float offsetX = Random.Range(0f, 100f);
        float offsetY = Random.Range(0f, 100f);
        for (int y = 0; y < settings.textureHeight; y++) {
            for (int x = 0; x < settings.textureWidth; x++) {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseValue = 0f;
                for (int i = 0; i < settings.noiseOctaves; i++) {
                    float sampleX = (x - halfWidth) / (float)settings.textureWidth * settings.noiseScale * frequency + offsetX;
                    float sampleY = (y - halfHeight) / (float)settings.textureHeight * settings.noiseScale * frequency + offsetY;
                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY);
                    noiseValue += perlinValue * amplitude;
                    amplitude *= settings.noisePersistence;
                    frequency *= settings.noiseLacunarity;
                }
                noiseValue = Mathf.Clamp01(noiseValue * settings.noiseIntensity);
                if (settings.invertNoise) { noiseValue = 1.0f - noiseValue; }
                Color pixelColor;
                float alpha = 1.0f;
                if (settings.useAlphaBlending) {
                    pixelColor = Color.white;
                    alpha = noiseValue;
                } else {
                    pixelColor = Color.Lerp(Color.black, settings.noiseTint, noiseValue);
                    alpha = 1.0f;
                }
                pixels[y * settings.textureWidth + x] = new Color(pixelColor.r, pixelColor.g, pixelColor.b, alpha);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
     }
}
