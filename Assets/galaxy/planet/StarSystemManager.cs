using UnityEngine;
using System.Collections.Generic;

public class StarSystemManager : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject starPrefab;
    public GameObject planetPrefab;

    [Header("Star System Settings")]
    [Range(0, 8)]
    public int minPlanets = 1;
    [Range(1, 10)]
    public int maxPlanets = 5;
    [Range(2f, 10f)]
    public float minOrbitRadius = 3f;
    [Range(5f, 20f)]
    public float maxOrbitRadius = 10f;
    [Range(0.5f, 5f)]
    public float planetScaleMin = 0.5f;
    [Range(1f, 10f)]
    public float planetScaleMax = 2f;

    [Header("Orbit Trace Settings")]
    public Material orbitTraceMaterial;
    public float orbitTraceWidth = 0.05f;
    [Range(0f, 1f)]
    public float orbitTraceAlpha = 0.3f;
    [Range(16, 64)]
    public int orbitTraceSegments = 32;
    public bool showOrbitTraces = true;

    [Header("References")]
    public Transform starSystemContainer; // Container to hold all the star system objects

    // Internal references
    private GameObject starInstance;
    private List<GameObject> planetInstances = new List<GameObject>();
    private List<LineRenderer> orbitTraceRenderers = new List<LineRenderer>();
    private StarSystemData linkedGalaxyStarData;
    private bool isActive = false;

    // Camera state when this star system was activated
    private Vector3 previousCameraPosition = new Vector3(0, 10, -10);
    private Quaternion previousCameraRotation = Quaternion.Euler(45, 0, 0);
    private bool hasSavedCameraState = false;

    // Camera controller state
    private Vector3 previousCameraTargetPosition;
    private float previousCameraDistance;
    private float previousCameraPitch;
    private float previousCameraAzimuth;
    private bool hasSavedCameraControllerState = false;

    public void Initialize(StarSystemData galaxyStarData)
    {
        linkedGalaxyStarData = galaxyStarData;
        
        // Set the initial state to inactive
        gameObject.SetActive(false);
        isActive = false;
        
        // If container doesn't exist, create it
        if (starSystemContainer == null)
        {
            GameObject container = new GameObject("StarSystemContainer");
            container.transform.SetParent(transform);
            starSystemContainer = container.transform;
            
            // Position it at the origin for consistent star system positioning
            starSystemContainer.position = Vector3.zero;
        }

        // Get configuration from GalaxyGenerator if available
        if (GalaxyGenerator.Instance != null)
        {
            GalaxyGenerator.StarSystemConfig config = GalaxyGenerator.Instance.solarSystemConfig;
            
            // Apply config settings
            minPlanets = config.minPlanetsPerSystem;
            maxPlanets = config.maxPlanetsPerSystem;
            minOrbitRadius = config.minOrbitRadius;
            maxOrbitRadius = config.maxOrbitRadius;
            planetScaleMin = config.minPlanetScale;
            planetScaleMax = config.maxPlanetScale;
            orbitTraceWidth = config.orbitTraceWidth;
            orbitTraceAlpha = config.orbitTraceAlpha;
            orbitTraceSegments = config.orbitTraceSegments;
        }

        // Create orbit trace material if not assigned
        if (orbitTraceMaterial == null)
        {
            // Create a default material for orbit traces
            orbitTraceMaterial = new Material(Shader.Find("Sprites/Default"));
            orbitTraceMaterial.color = new Color(1f, 1f, 1f, orbitTraceAlpha);
        }

        // Generate the star system with planets
        GenerateStarSystem();
    }

    public void GenerateStarSystem()
    {
        if (starPrefab == null || planetPrefab == null)
        {
            Debug.LogError("Star or Planet prefab is missing!", this);
            return;
        }

        ClearExistingSystem();

        // Create the star at the center
        starInstance = Instantiate(starPrefab, starSystemContainer.position, Quaternion.identity, starSystemContainer);
        starInstance.name = linkedGalaxyStarData != null ? $"Star_{linkedGalaxyStarData.systemName}" : "Star_Unknown";
        
        // Scale the star to be larger than planets
        float starScale = Random.Range(2.5f, 4.0f);
        starInstance.transform.localScale = new Vector3(starScale, starScale, starScale);

        // Choose a random number of planets within the range
        int numPlanets = Random.Range(minPlanets, maxPlanets + 1);
        
        // Generate planets in orbit around the star
        for (int i = 0; i < numPlanets; i++)
        {
            // Determine orbit radius
            float orbitRadius = Random.Range(minOrbitRadius, maxOrbitRadius);
            
            // Calculate a random position on that orbit
            float angle = Random.Range(0, 360) * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * orbitRadius;
            float z = Mathf.Sin(angle) * orbitRadius;
            
            // Random Y offset for visual interest
            float y = Random.Range(-1f, 1f);
            
            Vector3 planetPosition = new Vector3(x, y, z);
            
            // Create the planet
            GameObject planet = Instantiate(planetPrefab, starSystemContainer.position + planetPosition, Quaternion.identity, starSystemContainer);
            planet.name = $"Planet_{i + 1}";
            
            // Scale the planet
            float planetScale = Random.Range(planetScaleMin, planetScaleMax);
            planet.transform.localScale = new Vector3(planetScale, planetScale, planetScale);
            
            // Store reference to the planet
            planetInstances.Add(planet);
            
            // If the planet has a RandomPlanetGenerator component, generate a unique appearance
            RandomPlanetGenerator planetGenerator = planet.GetComponent<RandomPlanetGenerator>();
            if (planetGenerator != null)
            {
                // Randomize some planet properties
                planetGenerator.radius = Random.Range(0.8f, 1.2f); // Slight variation around base scale
                planetGenerator.noiseStrength = Random.Range(0.05f, 0.2f);
                planetGenerator.GeneratePlanet();
            }
            
            // Add PlanetInteraction component for handling clicks and interactions
            PlanetInteraction planetInteraction = planet.GetComponent<PlanetInteraction>();
            if (planetInteraction == null)
            {
                planetInteraction = planet.AddComponent<PlanetInteraction>();
            }
            
            // Initialize planet interaction properties
            planetInteraction.planetName = $"Planet_{linkedGalaxyStarData?.systemName}_{i + 1}";
            planetInteraction.planetIndex = i + 1;
            
            // Ensure the planet has a collider for interaction
            SphereCollider collider = planet.GetComponent<SphereCollider>();
            if (collider == null)
            {
                collider = planet.AddComponent<SphereCollider>();
                collider.radius = 0.5f; // Half the default scale
            }
            
            // Generate random properties for the planet
            planetInteraction.GenerateRandomProperties();

            // Create orbit trace visualization
            if (showOrbitTraces)
            {
                CreateOrbitTrace(orbitRadius, y, i);
            }
        }
    }

    private void CreateOrbitTrace(float radius, float yOffset, int planetIndex)
    {
        // Create a new GameObject for the orbit trace
        GameObject orbitTraceObject = new GameObject($"OrbitTrace_Planet_{planetIndex + 1}");
        orbitTraceObject.transform.SetParent(starSystemContainer);
        orbitTraceObject.transform.localPosition = Vector3.zero;

        // Add a LineRenderer component
        LineRenderer lineRenderer = orbitTraceObject.AddComponent<LineRenderer>();
        
        // Configure the LineRenderer
        lineRenderer.useWorldSpace = false;  // Use local space coordinates
        lineRenderer.startWidth = orbitTraceWidth;
        lineRenderer.endWidth = orbitTraceWidth;
        lineRenderer.positionCount = orbitTraceSegments + 1;  // +1 to close the loop
        lineRenderer.loop = true;  // Ensure a closed loop
        
        // Set material for the line
        lineRenderer.material = orbitTraceMaterial;
        
        // Set the color with transparency
        Color orbitColor = new Color(1f, 1f, 1f, orbitTraceAlpha);  // White with transparency
        lineRenderer.startColor = orbitColor;
        lineRenderer.endColor = orbitColor;
        
        // Create the orbit circle points
        for (int i = 0; i <= orbitTraceSegments; i++)
        {
            float angle = i * (2 * Mathf.PI / orbitTraceSegments);
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            Vector3 point = new Vector3(x, yOffset, z);
            lineRenderer.SetPosition(i, point);
        }
        
        // Store reference to the orbit trace
        orbitTraceRenderers.Add(lineRenderer);
    }

    private void ClearExistingSystem()
    {
        // Destroy existing star if any
        if (starInstance != null)
        {
            Destroy(starInstance);
            starInstance = null;
        }
        
        // Destroy existing planets if any
        foreach (GameObject planet in planetInstances)
        {
            if (planet != null)
            {
                Destroy(planet);
            }
        }
        planetInstances.Clear();
        
        // Destroy existing orbit traces
        foreach (LineRenderer orbitTrace in orbitTraceRenderers)
        {
            if (orbitTrace != null)
            {
                Destroy(orbitTrace.gameObject);
            }
        }
        orbitTraceRenderers.Clear();
    }

    public void Activate()
    {
        gameObject.SetActive(true);
        isActive = true;
        
        // Find and configure the camera to focus on the central star
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            CameraController cameraController = mainCamera.GetComponent<CameraController>();
            if (cameraController != null)
            {
                // Get the star position and center the camera on it
                Vector3 starPosition = GetStarPosition();
                
                // Explicitly set the target to the star position
                cameraController.SetTarget(starPosition);
                
                // Reset the camera distance and angles to provide a good view of the star system
                cameraController.currentDistance = 15f;
                cameraController.currentPitch = 45f;
                cameraController.currentAzimuth = 0f;
                
                // Apply the changes immediately
                cameraController.ResetCamera();
                
                Debug.Log($"Camera centered on star at position: {starPosition}, distance: {cameraController.currentDistance}");
                
                // Store the initial camera state in case we want to return to it later
                if (GalaxyGenerator.Instance != null)
                {
                    GalaxyGenerator.Instance.SetCurrentActiveStarSystem(this);
                }
            }
            else
            {
                // If no camera controller, at least point the camera at the star
                mainCamera.transform.position = GetStarPosition() + new Vector3(0, 10, -15);
                mainCamera.transform.LookAt(GetStarPosition());
                Debug.Log("No CameraController found, manually positioning camera");
            }
        }
        
        Debug.Log($"Star system {linkedGalaxyStarData?.systemName} activated");
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
        isActive = false;
    }

    public bool IsActive()
    {
        return isActive;
    }

    public StarSystemData GetLinkedGalaxyStarData()
    {
        return linkedGalaxyStarData;
    }

    // Toggle orbit traces visibility
    public void ToggleOrbitTraces(bool show)
    {
        showOrbitTraces = show;
        
        foreach (LineRenderer orbitTrace in orbitTraceRenderers)
        {
            if (orbitTrace != null)
            {
                orbitTrace.gameObject.SetActive(show);
            }
        }
    }

    // Store camera state when coming from the galaxy view
    public void StorePreviousCameraState(Vector3 position, Quaternion rotation)
    {
        previousCameraPosition = position;
        previousCameraRotation = rotation;
        hasSavedCameraState = true;
        Debug.Log($"Stored camera state at position: {position}, rotation: {rotation.eulerAngles}");
    }

    // Get the previously stored camera state
    public void GetPreviousCameraState(out Vector3 position, out Quaternion rotation)
    {
        if (hasSavedCameraState)
        {
            position = previousCameraPosition;
            rotation = previousCameraRotation;
            Debug.Log($"Retrieved camera state: {position}, rotation: {rotation.eulerAngles}");
        }
        else
        {
            // Default position/rotation if none was saved
            position = new Vector3(0, 10, -10); // Default camera position above and behind
            rotation = Quaternion.Euler(45, 0, 0); // Looking down at an angle
            Debug.Log("No saved camera state found, using defaults");
        }
    }

    // Store camera controller state
    public void StoreCameraControllerState(Vector3 targetPosition, float distance, float pitch, float azimuth)
    {
        previousCameraTargetPosition = targetPosition;
        previousCameraDistance = distance;
        previousCameraPitch = pitch;
        previousCameraAzimuth = azimuth;
        hasSavedCameraControllerState = true;
        Debug.Log($"Stored camera controller state: target={targetPosition}, distance={distance}, pitch={pitch}, azimuth={azimuth}");
    }

    // Get the previously stored camera controller state
    public void GetCameraControllerState(out Vector3 targetPosition, out float distance, out float pitch, out float azimuth)
    {
        if (hasSavedCameraControllerState)
        {
            targetPosition = previousCameraTargetPosition;
            distance = previousCameraDistance;
            pitch = previousCameraPitch;
            azimuth = previousCameraAzimuth;
            Debug.Log($"Retrieved camera controller state: target={targetPosition}, distance={distance}, pitch={pitch}, azimuth={azimuth}");
        }
        else
        {
            // Default values
            targetPosition = Vector3.zero;
            distance = 15f;
            pitch = 45f;
            azimuth = 0f;
            Debug.Log("No saved camera controller state found, using defaults");
        }
    }

    // Get the position of the star in this system
    public Vector3 GetStarPosition()
    {
        if (starInstance != null)
        {
            return starInstance.transform.position;
        }
        else
        {
            // If star instance doesn't exist yet, return the container position
            return starSystemContainer != null ? starSystemContainer.position : Vector3.zero;
        }
    }
}