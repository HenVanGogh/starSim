using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Centralized input handler for the entire game.
/// Attach to the same GameObject as GalaxyGenerator.
/// </summary>
public class InputHandler : MonoBehaviour
{
    // Singleton pattern
    public static InputHandler Instance { get; private set; }
    
    // References to other managers
    [Header("System References")]
    public GalaxyGenerator galaxyGenerator;
    
    // Current selections
    [Header("Current Selections")]
    private IClickableObject currentSelection;
    private IClickableObject lastSelection;
    
    // Events that other systems can subscribe to
    public event Action<IClickableObject> OnObjectClicked;
    public event Action<IClickableObject> OnObjectSelected;
    public event Action<IClickableObject> OnObjectDeselected;
    public event Action<IClickableObject> OnObjectHovered;
    public event Action<IClickableObject> OnObjectHoverExited;
    public event Action OnSelectionCleared;
    
    // Selection tracking by type
    private Dictionary<ClickableObjectType, IClickableObject> currentSelectionsByType = 
        new Dictionary<ClickableObjectType, IClickableObject>();
    
    // Time tracking for double-clicks
    private float lastClickTime;
    private IClickableObject lastClickedObject;
    [SerializeField] private float doubleClickTime = 0.3f;
    
    // UI states
    private bool isUIOpen = false;
    
    private void Awake()
    {
        // Implement singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
        
        // Find the galaxy generator if not assigned
        if (galaxyGenerator == null)
        {
            galaxyGenerator = GetComponent<GalaxyGenerator>();
            if (galaxyGenerator == null)
            {
                galaxyGenerator = FindAnyObjectByType<GalaxyGenerator>();
            }
        }
    }
    
    private void Update()
    {
        // Handle keyboard input
        HandleKeyboardInput();
        
        // Handle empty space clicks
        HandleEmptySpaceClicks();
    }
    
    /// <summary>
    /// Handle keyboard shortcuts
    /// </summary>
    private void HandleKeyboardInput()
    {
        // Clear selection with Escape key
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isUIOpen)
            {
                // Close UI windows first if any are open
                CloseAllUIWindows();
            }
            else if (currentSelection != null)
            {
                // Clear selection
                ClearSelection();
            }
            else if (galaxyGenerator != null && !galaxyGenerator.isGalaxyViewActive)
            {
                // Return to galaxy view
                galaxyGenerator.ReturnToGalaxyView();
            }
        }
        
        // Add other keyboard shortcuts here
    }
    
    /// <summary>
    /// Handles clicks on empty space to deselect
    /// </summary>
    private void HandleEmptySpaceClicks()
    {
        if (Input.GetMouseButtonDown(0))
        {
            // Cast a ray to see if we hit anything
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (!Physics.Raycast(ray, out hit))
            {
                // We didn't hit anything with a collider, so clear selections
                // But only if we're not interacting with UI
                if (!IsPointerOverUI())
                {
                    ClearSelection();
                }
            }
        }
    }
    
    /// <summary>
    /// Handle object click from any IClickableObject
    /// </summary>
    public void HandleObjectClicked(IClickableObject clickedObject)
    {
        if (clickedObject == null) return;
        
        // Notify listeners about the raw click event
        OnObjectClicked?.Invoke(clickedObject);
        
        // Check for double-click
        float timeSinceLastClick = Time.time - lastClickTime;
        bool isDoubleClick = (clickedObject == lastClickedObject && timeSinceLastClick < doubleClickTime);
        
        // Update click tracking
        lastClickTime = Time.time;
        lastClickedObject = clickedObject;
        
        if (isDoubleClick)
        {
            HandleDoubleClick(clickedObject);
            return;
        }
        
        // Handle selection changes
        if (currentSelection != clickedObject)
        {
            // Deselect previous object
            if (currentSelection != null)
            {
                currentSelection.SetSelected(false);
                OnObjectDeselected?.Invoke(currentSelection);
            }
            
            // Select new object
            currentSelection = clickedObject;
            clickedObject.SetSelected(true);
            
            // Update type-specific selection
            ClickableObjectType objectType = clickedObject.GetObjectType();
            if (currentSelectionsByType.ContainsKey(objectType))
            {
                currentSelectionsByType[objectType].SetSelected(false);
            }
            currentSelectionsByType[objectType] = clickedObject;
            
            // Notify listeners
            OnObjectSelected?.Invoke(clickedObject);
        }
        else
        {
            // Toggle selection of current object
            clickedObject.ToggleSelection();
            
            if (clickedObject.isSelected)
            {
                OnObjectSelected?.Invoke(clickedObject);
            }
            else
            {
                OnObjectDeselected?.Invoke(clickedObject);
                currentSelection = null;
                
                // Remove from type-specific selections if deselected
                ClickableObjectType objectType = clickedObject.GetObjectType();
                if (currentSelectionsByType.ContainsKey(objectType) && 
                    currentSelectionsByType[objectType] == clickedObject)
                {
                    currentSelectionsByType.Remove(objectType);
                }
            }
        }
        
        // Store last selected object for tracking
        lastSelection = clickedObject;
    }
    
    /// <summary>
    /// Handle double-click on object
    /// </summary>
    private void HandleDoubleClick(IClickableObject clickedObject)
    {
        // Different behaviors based on object type
        switch (clickedObject.GetObjectType())
        {
            case ClickableObjectType.Star:
                // Navigate to star system if it's a StarSystemData
                if (clickedObject is StarSystemData starData)
                {
                    starData.NavigateToStarSystem();
                }
                break;
                
            case ClickableObjectType.Planet:
                // Zoom to planet or show detailed view
                Debug.Log($"Double-clicked on planet: {(clickedObject as PlanetInteraction)?.planetName}");
                break;
                
            default:
                // Default double-click behavior
                Debug.Log("Double-clicked on object: " + clickedObject.GetType().Name);
                break;
        }
    }
    
    /// <summary>
    /// Handle hover enter from any IClickableObject
    /// </summary>
    public void HandleObjectHoverEnter(IClickableObject hoveredObject)
    {
        if (hoveredObject == null) return;
        
        // Notify listeners
        OnObjectHovered?.Invoke(hoveredObject);
    }
    
    /// <summary>
    /// Handle hover exit from any IClickableObject
    /// </summary>
    public void HandleObjectHoverExit(IClickableObject exitedObject)
    {
        if (exitedObject == null) return;
        
        // Notify listeners
        OnObjectHoverExited?.Invoke(exitedObject);
    }
    
    /// <summary>
    /// Clear all selections
    /// </summary>
    public void ClearSelection()
    {
        // Deselect current object
        if (currentSelection != null)
        {
            currentSelection.SetSelected(false);
            OnObjectDeselected?.Invoke(currentSelection);
            currentSelection = null;
        }
        
        // Clear all type-specific selections
        foreach (var obj in currentSelectionsByType.Values)
        {
            obj.SetSelected(false);
        }
        currentSelectionsByType.Clear();
        
        // Notify listeners
        OnSelectionCleared?.Invoke();
    }
    
    /// <summary>
    /// Get the currently selected object of a specific type
    /// </summary>
    public IClickableObject GetSelectedObjectOfType(ClickableObjectType type)
    {
        if (currentSelectionsByType.TryGetValue(type, out IClickableObject obj))
        {
            return obj;
        }
        return null;
    }
    
    /// <summary>
    /// Check if a UI element is under the pointer
    /// </summary>
    private bool IsPointerOverUI()
    {
        return UnityEngine.EventSystems.EventSystem.current != null &&
               UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }
    
    /// <summary>
    /// Close all UI windows
    /// </summary>
    private void CloseAllUIWindows()
    {
        isUIOpen = false;
        // Implement UI closing logic here
    }
}