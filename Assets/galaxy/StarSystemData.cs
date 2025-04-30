using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Renderer))] // Ensure there's a Renderer to change material
public class StarSystemData : MonoBehaviour
{
    // Static reference to the galaxy manager for navigation
    public static GalaxyGenerator galaxyManager;

    [Header("System Info")]
    public string systemName = "Unnamed System";

    [Header("Gameplay Data")]
    public Color starColor = Color.white;

    // Reference to detailed star system
    private StarSystemManager detailedSystem;

    // Camera position and rotation when entering the star system
    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool hasSavedCameraState = false;

    // Neighbors in the galaxy graph
    private List<StarSystemData> neighbors = new List<StarSystemData>();

    // Original material (to restore after highlighting)
    private Material originalMaterial;
    private Renderer starRenderer;

    private void Awake()
    {
        // Cache the renderer component
        starRenderer = GetComponent<Renderer>();
        if (starRenderer != null) {
            originalMaterial = starRenderer.material;
        }
    }

    // Initialize with name and color
    public void Initialize(string name)
    {
        systemName = name;
        
        // Set a random star color
        SetRandomStarColor();
        
        // Apply the star color to the renderer
        ApplyStarColor();
    }

    // Set a random star color (based on stellar classification)
    private void SetRandomStarColor()
    {
        // Array of common star colors (from blue to red)
        Color[] starColors = new Color[] {
            new Color(0.6f, 0.8f, 1.0f),  // Blue
            new Color(0.9f, 0.9f, 1.0f),  // White-Blue
            new Color(1.0f, 1.0f, 0.9f),  // White
            new Color(1.0f, 0.95f, 0.8f), // Yellow-White
            new Color(1.0f, 0.9f, 0.6f),  // Yellow
            new Color(1.0f, 0.7f, 0.3f),  // Orange
            new Color(1.0f, 0.5f, 0.2f)   // Red
        };
        
        // Select a random color
        starColor = starColors[Random.Range(0, starColors.Length)];
    }
    
    // Apply the star color to the renderer material
    private void ApplyStarColor()
    {
        if (starRenderer != null)
        {
            // Create a new material instance to avoid modifying the shared material
            Material newMaterial = new Material(starRenderer.material);
            newMaterial.color = starColor;
            starRenderer.material = newMaterial;
            originalMaterial = newMaterial;
        }
    }

    // Set the detailed star system reference
    public void SetDetailedSystem(StarSystemManager system)
    {
        detailedSystem = system;
    }

    // Store camera position and rotation for later restoration
    public void StorePreviousCameraState(Vector3 position, Quaternion rotation)
    {
        previousCameraPosition = position;
        previousCameraRotation = rotation;
        hasSavedCameraState = true;
    }

    // Get the previously stored camera state
    public void GetPreviousCameraState(out Vector3 position, out Quaternion rotation)
    {
        if (hasSavedCameraState)
        {
            position = previousCameraPosition;
            rotation = previousCameraRotation;
        }
        else
        {
            // Default position/rotation if none was saved
            position = new Vector3(0, 10, -10); // Default camera position above and behind
            rotation = Quaternion.Euler(45, 0, 0); // Looking down at an angle
        }
    }
    
    // Called when this star is clicked
    private void OnMouseDown()
    {
        // Inform the galaxy manager about the selection
        if (galaxyManager != null)
        {
            galaxyManager.HandleStarSelection(this);
            
            // Check for left mouse button specifically
            if (Input.GetMouseButton(0))
            {
                // Navigate to the detailed star system view
                NavigateToStarSystem();
            }
        }
    }
    
    // Navigate to this star's detailed system view
    public void NavigateToStarSystem()
    {
        if (detailedSystem != null)
        {
            // Store current camera position BEFORE any changes are made
            if (galaxyManager != null && galaxyManager.mainCamera != null)
            {
                // Save the current camera position and rotation for later
                Camera cam = galaxyManager.mainCamera;
                StorePreviousCameraState(cam.transform.position, cam.transform.rotation);
                Debug.Log($"Saved camera position before navigating to {systemName}: Position={cam.transform.position}, Rotation={cam.transform.rotation.eulerAngles}");
                
                // Store CameraController state if it exists
                CameraController cameraController = cam.GetComponent<CameraController>();
                if (cameraController != null)
                {
                    Debug.Log($"Saving CameraController target position: {cameraController.targetPosition}");
                    detailedSystem.StoreCameraControllerState(cameraController.targetPosition, 
                                                              cameraController.currentDistance, 
                                                              cameraController.currentPitch, 
                                                              cameraController.currentAzimuth);
                }
            }
            
            // If we've already generated this star system, just activate it
            detailedSystem.Activate();
            
            // Center the camera on the star - do this AFTER activation
            if (galaxyManager != null && galaxyManager.mainCamera != null)
            {
                // Get the star position
                Vector3 starPosition = detailedSystem.GetStarPosition();
                Debug.Log($"Star position in {systemName}: {starPosition}");
                
                // Check if camera has a CameraController component
                Camera cam = galaxyManager.mainCamera;
                CameraController cameraController = cam.GetComponent<CameraController>();
                
                if (cameraController != null)
                {
                    // Use the camera controller to position the camera
                    cameraController.targetPosition = starPosition;
                    cameraController.initialDistance = 20f;
                    cameraController.initialPitch = 45f;
                    cameraController.targetAzimuth = 0f;
                    
                    // Force immediate update of camera position
                    cameraController.ResetCamera();
                    
                    Debug.Log($"Camera positioned using CameraController to look at star at {starPosition}");
                }
                else
                {
                    // Fallback to direct camera positioning if no controller exists
                    float distance = 20f; // Distance from the star
                    
                    // Position camera to look at the star from an angle
                    Vector3 cameraPosition = starPosition + new Vector3(0, distance * 0.5f, -distance);
                    cam.transform.position = cameraPosition;
                    
                    // Look at the star
                    cam.transform.LookAt(starPosition);
                    
                    Debug.Log($"Camera positioned at {cam.transform.position} looking at star at {starPosition}");
                }
            }
            
            // Deactivate the galaxy view
            if (galaxyManager != null)
            {
                galaxyManager.DeactivateGalaxyView();
                galaxyManager.SetCurrentActiveStarSystem(detailedSystem);
            }
        }
        else
        {
            Debug.LogError($"Star system for {systemName} was not pre-generated", this);
        }
    }
    
    // Return to galaxy view
    public void ReturnToGalaxyView()
    {
        if (detailedSystem != null)
        {
            detailedSystem.Deactivate();
        }
        
        // Activate the galaxy view
        if (galaxyManager != null)
        {
            galaxyManager.ActivateGalaxyView();
        }
    }

    // Add a neighboring star
    public void AddNeighbour(StarSystemData neighbour)
    {
        if (neighbour != null && !neighbors.Contains(neighbour))
        {
            neighbors.Add(neighbour);
        }
    }
    
    // Clear all neighbors
    public void ClearNeighbours()
    {
        neighbors.Clear();
    }
    
    // Highlight this star as selected
    public void HighlightSelected()
    {
        if (starRenderer != null && galaxyManager?.selectedMaterial != null)
        {
            starRenderer.material = galaxyManager.selectedMaterial;
        }
    }
    
    // Highlight this star as a neighbor
    public void HighlightNeighbour()
    {
        if (starRenderer != null && galaxyManager?.neighbourMaterial != null)
        {
            starRenderer.material = galaxyManager.neighbourMaterial;
        }
    }
    
    // Remove highlighting
    public void Unhighlight()
    {
        if (starRenderer != null && originalMaterial != null)
        {
            starRenderer.material = originalMaterial;
        }
    }
}