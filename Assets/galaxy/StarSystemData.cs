using UnityEngine;

/// <summary>
/// Data component attached to each star in the galaxy view.
/// Handles selection, highlighting, and navigation to detailed view.
/// </summary>
public class StarSystemData : MonoBehaviour, IClickableObject
{
    // --- Static References ---
    public static GalaxyGenerator galaxyManager;

    // --- Inspector Fields ---
    [Header("Star Data")]
    public string systemName = "Unnamed System";
    public int systemResourceValue = 0;
    public float systemHabitability = 0f;
    
    [Header("Star Type")]
    public StarType starType = StarType.MainSequence;
    
    // Detailed system reference
    private StarSystemManager detailedSystem;
    
    // Selection state
    [SerializeField] private bool _isSelected = false;
    
    // Implementation of IClickableObject interface
    public bool isSelected 
    { 
        get { return _isSelected; } 
        set { 
            _isSelected = value;
            UpdateVisualState();
        }
    }
    
    // Cached references
    private Renderer starRenderer;
    private Material originalMaterial;
    private InputHandler inputHandler;
    
    // Cached camera state for returning from detailed view
    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool hasSavedCameraState = false;
    
    // List of neighbouring stars (connected by hyperlanes)
    private System.Collections.Generic.List<StarSystemData> neighbours = 
        new System.Collections.Generic.List<StarSystemData>();

    private void Awake()
    {
        // Cache renderer reference
        starRenderer = GetComponent<Renderer>();
        if (starRenderer != null)
        {
            originalMaterial = starRenderer.material;
        }
        
        // Find the input handler
        inputHandler = FindAnyObjectByType<InputHandler>();
        if (inputHandler == null)
        {
            Debug.LogWarning("InputHandler not found in the scene!", this);
        }
    }

    /// <summary>
    /// Initialize the star system with a name.
    /// </summary>
    public void Initialize(string name)
    {
        systemName = name;
        
        // Generate random properties
        GenerateRandomProperties();
    }
    
    /// <summary>
    /// Generate random properties for this star system
    /// </summary>
    private void GenerateRandomProperties()
    {
        // Random resource value from 1-10
        systemResourceValue = Random.Range(1, 11);
        
        // Random habitability from 0-1
        systemHabitability = Random.value;
        
        // Random star type
        starType = (StarType)Random.Range(0, System.Enum.GetValues(typeof(StarType)).Length);
    }

    /// <summary>
    /// Handles mouse click on this star system.
    /// </summary>
    private void OnMouseDown()
    {
        // Use the InputHandler to handle the click
        if (inputHandler != null)
        {
            inputHandler.HandleObjectClicked(this);
        }
        else
        {
            // Fallback behavior if input handler not found
            if (galaxyManager != null)
            {
                galaxyManager.HandleStarSelection(this);
            }
        }
    }
    
    /// <summary>
    /// Handle mouse hover enter
    /// </summary>
    private void OnMouseEnter()
    {
        // Notify the input handler
        if (inputHandler != null)
        {
            inputHandler.HandleObjectHoverEnter(this);
        }
    }
    
    /// <summary>
    /// Handle mouse hover exit
    /// </summary>
    private void OnMouseExit()
    {
        // Notify the input handler
        if (inputHandler != null)
        {
            inputHandler.HandleObjectHoverExit(this);
        }
    }
    
    /// <summary>
    /// Gets the type of object this is for the input handler
    /// </summary>
    public ClickableObjectType GetObjectType()
    {
        return ClickableObjectType.Star;
    }
    
    /// <summary>
    /// Toggle the selection state
    /// </summary>
    public void ToggleSelection()
    {
        isSelected = !isSelected;
        UpdateVisualState();
    }
    
    /// <summary>
    /// Set the selected state of this star
    /// </summary>
    public void SetSelected(bool selected, bool updateVisuals = true)
    {
        isSelected = selected;
        if (updateVisuals)
        {
            UpdateVisualState();
        }
    }
    
    /// <summary>
    /// Update visual state based on selection
    /// </summary>
    private void UpdateVisualState()
    {
        if (isSelected)
        {
            HighlightSelected();
        }
        else
        {
            Unhighlight();
        }
    }

    /// <summary>
    /// Highlight this star as selected (called by GalaxyGenerator).
    /// </summary>
    public void HighlightSelected()
    {
        if (starRenderer != null && galaxyManager != null && galaxyManager.selectedMaterial != null)
        {
            starRenderer.material = galaxyManager.selectedMaterial;
        }
    }

    /// <summary>
    /// Highlight this star as a neighbour of the selected star (called by GalaxyGenerator).
    /// </summary>
    public void HighlightNeighbour()
    {
        if (starRenderer != null && galaxyManager != null && galaxyManager.neighbourMaterial != null)
        {
            starRenderer.material = galaxyManager.neighbourMaterial;
        }
    }

    /// <summary>
    /// Remove any highlighting.
    /// </summary>
    public void Unhighlight()
    {
        if (starRenderer != null && originalMaterial != null)
        {
            starRenderer.material = originalMaterial;
        }
    }

    /// <summary>
    /// Add a neighbour star connected by a hyperlane.
    /// </summary>
    public void AddNeighbour(StarSystemData neighbour)
    {
        if (neighbour != null && !neighbours.Contains(neighbour))
        {
            neighbours.Add(neighbour);
        }
    }

    /// <summary>
    /// Remove a neighbour from the connections.
    /// </summary>
    public void RemoveNeighbour(StarSystemData neighbour)
    {
        if (neighbour != null)
        {
            neighbours.Remove(neighbour);
        }
    }

    /// <summary>
    /// Clear all neighbour connections.
    /// </summary>
    public void ClearNeighbours()
    {
        neighbours.Clear();
    }

    /// <summary>
    /// Get reference to all neighbouring stars.
    /// </summary>
    public System.Collections.Generic.List<StarSystemData> GetNeighbours()
    {
        return neighbours;
    }

    /// <summary>
    /// Link this star to a detailed star system view
    /// </summary>
    public void SetDetailedSystem(StarSystemManager system)
    {
        detailedSystem = system;
    }

    /// <summary>
    /// Navigate to the detailed view of this star system
    /// </summary>
    public void NavigateToStarSystem()
    {
        if (galaxyManager == null)
        {
            Debug.LogError("Cannot navigate: galaxy manager reference is null");
            return;
        }

        // Save camera position before navigating
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            previousCameraPosition = mainCam.transform.position;
            previousCameraRotation = mainCam.transform.rotation;
            hasSavedCameraState = true;
            
            // Save camera controller state if available
            CameraController cameraController = mainCam.GetComponent<CameraController>();
            if (cameraController != null)
            {
                // Store the camera state in the galaxy manager for retrieval when returning to galaxy view
                if (galaxyManager != null)
                {
                    galaxyManager.StoreCameraStateForReturn(
                        cameraController.targetPosition,
                        cameraController.currentDistance,
                        cameraController.currentPitch,
                        cameraController.currentAzimuth
                    );
                }
                
                // Also store in the detailed system if available
                if (detailedSystem != null)
                {
                    detailedSystem.StoreCameraControllerState(
                        cameraController.targetPosition,
                        cameraController.currentDistance,
                        cameraController.currentPitch,
                        cameraController.currentAzimuth
                    );
                }
            }
        }
        
        if (detailedSystem != null)
        {
            // Tell the galaxy manager to deactivate the galaxy view
            galaxyManager.DeactivateGalaxyView();
            
            // Track this star as the selected star
            galaxyManager.currentlySelectedStar = this;
            
            // Tell galaxy manager which star system is now active
            galaxyManager.SetCurrentActiveStarSystem(detailedSystem);
            
            // Activate the detailed star system
            detailedSystem.Activate();
            
            Debug.Log($"Navigated to star system: {systemName}");
        }
        else
        {
            Debug.LogWarning($"Star system {systemName} has no detailed view assigned");
        }
    }

    /// <summary>
    /// Return from star system view to galaxy view
    /// </summary>
    public void ReturnToGalaxyView()
    {
        if (galaxyManager != null)
        {
            // Let the galaxy manager handle returning to galaxy view
            galaxyManager.ActivateGalaxyView();
            Debug.Log($"Returned to galaxy view from {systemName}");
        }
    }
    
    /// <summary>
    /// Store camera state
    /// </summary>
    public void StorePreviousCameraState(Vector3 position, Quaternion rotation)
    {
        previousCameraPosition = position;
        previousCameraRotation = rotation;
        hasSavedCameraState = true;
    }
    
    /// <summary>
    /// Get previously stored camera state
    /// </summary>
    public void GetPreviousCameraState(out Vector3 position, out Quaternion rotation)
    {
        if (hasSavedCameraState)
        {
            position = previousCameraPosition;
            rotation = previousCameraRotation;
        }
        else
        {
            position = new Vector3(0, 10, -10); // Default position above
            rotation = Quaternion.Euler(45, 0, 0); // Looking down at 45 degrees
        }
    }
}

/// <summary>
/// Types of stars in the galaxy
/// </summary>
public enum StarType
{
    MainSequence,
    RedGiant,
    WhiteDwarf,
    Neutron,
    BlackHole,
    Binary
}