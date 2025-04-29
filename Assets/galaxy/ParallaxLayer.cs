using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    [Tooltip("How much this layer moves relative to the camera. 0 = no movement, 1 = moves with camera. Smaller values for layers further away.")]
    [Range(0f, 1f)]
    public float parallaxFactor = 0.1f;

    [Tooltip("Optional: Set to true if the texture should also scroll based on camera movement.")]
    public bool scrollTexture = true;

    [Tooltip("How much the texture scrolls relative to the parallax factor.")]
    public float textureScrollMultiplier = 1.0f; // Adjust texture scroll speed

    private Transform cameraTransform;
    private Vector3 initialPosition;
    private Vector2 initialTextureOffset;
    private Material layerMaterial; // Reference to the material for texture scrolling

    void Start()
    {
        // Find the main camera
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
        else
        {
            Debug.LogError("ParallaxLayer: Main Camera not found! Ensure your camera is tagged 'MainCamera'.", this);
            enabled = false; // Disable script if no camera
            return;
        }

        // Store the starting position of this layer relative to the world origin
        initialPosition = transform.position;

        // Get the material if texture scrolling is enabled
        if (scrollTexture)
        {
            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null && renderer.material != null) // Use sharedMaterial if you want all instances to scroll together, material for individual scrolling
            {
                layerMaterial = renderer.material; // Get instance of material
                initialTextureOffset = layerMaterial.mainTextureOffset;
            }
            else
            {
                Debug.LogWarning("ParallaxLayer: Renderer or Material not found for texture scrolling.", this);
                scrollTexture = false; // Disable texture scrolling if no material
            }
        }
    }

    void LateUpdate()
    {
        // Ensure camera reference is still valid
        if (cameraTransform == null) return;

        // Calculate the parallax offset based on camera movement from origin
        // We use the camera's position directly, assuming the background layers start relative to (0,0,0)
        Vector3 cameraDisplacement = cameraTransform.position; // Displacement from world origin
        Vector3 parallaxOffset = cameraDisplacement * parallaxFactor;

        // Apply the parallax offset to the layer's position
        // The layer moves *less* than the camera by the parallax factor
        transform.position = initialPosition + parallaxOffset;


        // --- Optional Texture Scrolling ---
        if (scrollTexture && layerMaterial != null)
        {
            // Calculate texture offset based on the parallax offset
            // Use X and Y displacement, scale by multiplier
            // Divide by transform scale if needed, depending on UVs and tiling
            float textureOffsetX = (parallaxOffset.x / transform.localScale.x) * textureScrollMultiplier;
            float textureOffsetY = (parallaxOffset.y / transform.localScale.y) * textureScrollMultiplier; // Use Y or Z depending on quad orientation

            // Apply the new texture offset
            layerMaterial.mainTextureOffset = initialTextureOffset + new Vector2(textureOffsetX, textureOffsetY);
        }
    }
}
