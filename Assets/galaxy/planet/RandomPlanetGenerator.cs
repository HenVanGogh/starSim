using UnityEngine;
using System.Collections.Generic;

// Ensure this script can be added as a component
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class RandomPlanetGenerator : MonoBehaviour
{
    // Enum to define noise presets
    public enum NoisePreset
    {
        LowFrequency,
        MediumFrequency,
        HighFrequency
    }

    [Header("Planet Settings")]
    [Range(0.1f, 100f)]
    public float radius = 1f; // Radius of the planet
    [Range(1, 256)]
    public int detailLevel = 64; // Number of subdivisions for the sphere mesh (higher = more detailed)
    public Material planetMaterial; // URP Material to apply to the planet

    [Header("Terrain Noise Settings")]
    public NoisePreset noisePreset = NoisePreset.MediumFrequency; // Select a noise preset
    [Range(1, 8)]
    public int noiseLayers = 4; // Number of noise layers
    [Range(0.1f, 10f)]
    public float persistence = 0.5f; // Controls how much each octave contributes to the overall shape
    [Range(1f, 4f)]
    public float lacunarity = 2f; // Controls the increase in frequency for each octave
    public float noiseStrength = 0.1f; // Overall strength of the noise effect

    [Header("Color Settings")]
    public Gradient planetColorGradient; // Gradient for multicolor based on height

    private Mesh mesh; // The mesh of the planet

    // Noise parameters based on presets
    private float baseFrequency;
    private float baseAmplitude;

    void Start()
    {
        // Generate the planet when the script starts
        GeneratePlanet();
    }

    // Method to generate the planet
    [ContextMenu("Generate Planet")] // Add a context menu option in the inspector
    public void GeneratePlanet()
    {
        // Get or create the MeshFilter and MeshRenderer components
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

        // Initialize the mesh
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "PlanetMesh";
        }
        else
        {
            mesh.Clear(); // Clear existing mesh data
        }

        // Assign the mesh to the MeshFilter
        meshFilter.mesh = mesh;

        // Assign the material to the MeshRenderer
        if (planetMaterial != null)
        {
            meshRenderer.material = planetMaterial;
        }
        else
        {
            Debug.LogWarning("Planet Material is not assigned. Please assign a URP material.");
        }

        // Determine noise parameters based on the selected preset
        SetNoiseParameters();

        // Generate the sphere mesh
        CreateSphereMesh();

        // Apply noise to the mesh vertices
        ApplyNoiseToMesh();

        // Apply multicolor based on height using the gradient
        ApplyMulticolorByHeight();

        // Recalculate normals and tangents for proper lighting
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
    }

    // Set noise parameters based on the selected preset
    private void SetNoiseParameters()
    {
        switch (noisePreset)
        {
            case NoisePreset.LowFrequency:
                baseFrequency = 0.5f;
                baseAmplitude = 0.5f;
                break;
            case NoisePreset.MediumFrequency:
                baseFrequency = 1f;
                baseAmplitude = 1f;
                break;
            case NoisePreset.HighFrequency:
                baseFrequency = 2f;
                baseAmplitude = 0.8f;
                break;
        }
    }

    // Create a sphere mesh using a subdivided cube approach
    private void CreateSphereMesh()
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uv = new List<Vector2>();

        // This is a placeholder for a proper sphere generation algorithm.
        // A common approach is to start with an icosahedron and subdivide its triangles.
        // For simplicity here, we'll use a basic sphere generation based on polar coordinates,
        // which is easier to implement quickly but can have pinching at the poles.

        int latitudeBands = detailLevel;
        int longitudeBands = detailLevel;

        for (int lat = 0; lat <= latitudeBands; lat++)
        {
            float theta = lat * Mathf.PI / latitudeBands;
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);

            for (int lon = 0; lon <= longitudeBands; lon++)
            {
                float phi = lon * 2 * Mathf.PI / longitudeBands;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                float x = cosPhi * sinTheta;
                float y = cosTheta;
                float z = sinPhi * sinTheta;

                Vector3 vertex = new Vector3(x, y, z) * radius;
                vertices.Add(vertex);
                uv.Add(new Vector2((float)lon / longitudeBands, (float)lat / latitudeBands));

                if (lat < latitudeBands && lon < longitudeBands)
                {
                    int first = (lat * (longitudeBands + 1)) + lon;
                    int second = first + longitudeBands + 1;

                    triangles.Add(first);
                    triangles.Add(second);
                    triangles.Add(first + 1);

                    triangles.Add(second);
                    triangles.Add(second + 1);
                    triangles.Add(first + 1);
                }
            }
        }


        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uv.ToArray();
    }


    // Apply noise to the mesh vertices
    private void ApplyNoiseToMesh()
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] displacedVertices = new Vector3[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            float noiseValue = 0;
            float frequency = baseFrequency;
            float amplitude = baseAmplitude;

            // Apply multiple layers of noise (octaves)
            for (int j = 0; j < noiseLayers; j++)
            {
                // Use Perlin noise based on the vertex position
                // Offset the position slightly to get different noise values for each vertex
                float sampleX = (vertex.x + 100f) * frequency;
                float sampleY = (vertex.y + 100f) * frequency;
                float sampleZ = (vertex.z + 100f) * frequency;

                // Get 3D Perlin noise value (range -1 to 1)
                float perlin = PerlinNoise3D(sampleX, sampleY, sampleZ);

                noiseValue += perlin * amplitude;

                // Increase frequency and decrease amplitude for the next octave
                frequency *= lacunarity;
                amplitude *= persistence;
            }

            // Displace the vertex along its normal (outward direction from the center)
            Vector3 direction = vertex.normalized;
            displacedVertices[i] = vertex + direction * noiseValue * noiseStrength;
        }

        mesh.vertices = displacedVertices;
    }

    // Basic 3D Perlin Noise implementation (can be replaced with Unity's Mathf.PerlinNoise if needed, but 3D is better for spheres)
    // Note: This is a simplified example. For high-quality terrain, consider a dedicated noise library or a more robust implementation.
    float PerlinNoise3D(float x, float y, float z)
    {
        // A simple way to combine 2D Perlin noise calls to approximate 3D noise
        // This is not true 3D Perlin noise but can work for basic effects.
        // For better results, consider a true 3D noise function.
        float ab = Mathf.PerlinNoise(x, y);
        float bc = Mathf.PerlinNoise(y, z);
        float ca = Mathf.PerlinNoise(z, x);
        float ba = Mathf.PerlinNoise(y, x);
        float cb = Mathf.PerlinNoise(z, y);
        float ac = Mathf.PerlinNoise(x, z);

        return (ab + bc + ca + ba + cb + ac) / 6.0f * 2.0f - 1.0f; // Normalize to approximately -1 to 1
    }

    // Apply multicolor based on vertex height using a gradient
    public void ApplyMulticolorByHeight()
    {
        Vector3[] vertices = mesh.vertices;
        Color[] colors = new Color[vertices.Length];
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;

        // Find min and max height after noise displacement
        for (int i = 0; i < vertices.Length; i++)
        {
            float height = vertices[i].magnitude; // Distance from center
            if (height < minHeight) minHeight = height;
            if (height > maxHeight) maxHeight = height;
        }

        // Assign color based on normalized height using the gradient
        for (int i = 0; i < vertices.Length; i++)
        {
            float height = vertices[i].magnitude;
            float normalizedHeight = Mathf.InverseLerp(minHeight, maxHeight, height); // 0 to 1 range
            colors[i] = planetColorGradient.Evaluate(normalizedHeight); // Get color from gradient
        }

        mesh.colors = colors;
    }

    // The previous ColorVerticesByHeight is now replaced by ApplyMulticolorByHeight which uses a gradient.
    // You can remove the old ColorVerticesByHeight method if you no longer need it as a separate example.
}
