using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Legacy input manager class now used only as a bridge to the new InputHandler system.
/// Consider transitioning completely to InputHandler for future development.
/// </summary>
[System.Obsolete("Use InputHandler instead")]
public class InputManager : MonoBehaviour
{
    // Singleton pattern
    public static InputManager Instance { get; private set; }
    
    // References to currently selected objects
    [Header("Current Selections")]
    public StarSystemData currentlySelectedStar;
    public PlanetInteraction currentlySelectedPlanet;
    
    // Events that other systems can subscribe to
    public static event Action<StarSystemData> OnStarSystemSelected;
    public static event Action<PlanetInteraction> OnPlanetSelected;
    public static event Action<PlanetInteraction> OnPlanetHovered;
    public static event Action<PlanetInteraction> OnPlanetHoverExited;
    public static event Action OnSelectionCleared;
    
    [Header("System References")]
    [Tooltip("The GalaxyGenerator reference")]
    public GalaxyGenerator galaxyGenerator;
    public InputHandler inputHandler;
    
    private void Awake()
    {
        // Singleton pattern implementation
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
        
        // Find the galaxy generator if not assigned
        if (galaxyGenerator == null)
        {
            galaxyGenerator = FindAnyObjectByType<GalaxyGenerator>();
        }
        
        // Find the input handler
        if (inputHandler == null)
        {
            inputHandler = FindAnyObjectByType<InputHandler>();
        }
    }
    
    private void OnEnable()
    {
        // Subscribe to InputHandler events instead
        if (inputHandler != null)
        {
            inputHandler.OnObjectSelected += OnObjectSelected;
            inputHandler.OnObjectDeselected += OnObjectDeselected;
            inputHandler.OnObjectHovered += OnObjectHovered;
            inputHandler.OnObjectHoverExited += OnObjectHoverExited;
            inputHandler.OnSelectionCleared += OnInputHandlerSelectionCleared;
        }
    }
    
    private void OnDisable()
    {
        // Unsubscribe from InputHandler events
        if (inputHandler != null)
        {
            inputHandler.OnObjectSelected -= OnObjectSelected;
            inputHandler.OnObjectDeselected -= OnObjectDeselected;
            inputHandler.OnObjectHovered -= OnObjectHovered;
            inputHandler.OnObjectHoverExited -= OnObjectHoverExited;
            inputHandler.OnSelectionCleared -= OnInputHandlerSelectionCleared;
        }
    }
    
    // Bridge handlers to convert from InputHandler events to legacy events
    private void OnObjectSelected(IClickableObject obj)
    {
        if (obj.GetObjectType() == ClickableObjectType.Star)
        {
            currentlySelectedStar = obj as StarSystemData;
            OnStarSystemSelected?.Invoke(currentlySelectedStar);
        }
        else if (obj.GetObjectType() == ClickableObjectType.Planet)
        {
            currentlySelectedPlanet = obj as PlanetInteraction;
            OnPlanetSelected?.Invoke(currentlySelectedPlanet);
        }
    }
    
    private void OnObjectDeselected(IClickableObject obj)
    {
        if (obj.GetObjectType() == ClickableObjectType.Star)
        {
            currentlySelectedStar = null;
        }
        else if (obj.GetObjectType() == ClickableObjectType.Planet)
        {
            currentlySelectedPlanet = null;
        }
    }
    
    private void OnObjectHovered(IClickableObject obj)
    {
        if (obj.GetObjectType() == ClickableObjectType.Planet)
        {
            OnPlanetHovered?.Invoke(obj as PlanetInteraction);
        }
    }
    
    private void OnObjectHoverExited(IClickableObject obj)
    {
        if (obj.GetObjectType() == ClickableObjectType.Planet)
        {
            OnPlanetHoverExited?.Invoke(obj as PlanetInteraction);
        }
    }
    
    private void OnInputHandlerSelectionCleared()
    {
        currentlySelectedStar = null;
        currentlySelectedPlanet = null;
        OnSelectionCleared?.Invoke();
    }
    
    /// <summary>
    /// Handles star system selection - now forwards to InputHandler
    /// </summary>
    public void HandleStarSystemSelected(StarSystemData star)
    {
        if (star == null || inputHandler == null) return;
        
        inputHandler.HandleObjectClicked(star);
    }
    
    /// <summary>
    /// Clears the selected planet
    /// </summary>
    public void ClearPlanetSelection()
    {
        if (inputHandler != null && currentlySelectedPlanet != null)
        {
            currentlySelectedPlanet.SetSelected(false);
            currentlySelectedPlanet = null;
        }
    }
    
    /// <summary>
    /// Clears the selected star
    /// </summary>
    public void ClearStarSelection()
    {
        if (inputHandler != null && currentlySelectedStar != null)
        {
            currentlySelectedStar.SetSelected(false);
            currentlySelectedStar = null;
        }
    }
    
    /// <summary>
    /// Clears all selections
    /// </summary>
    public void ClearAllSelections()
    {
        if (inputHandler != null)
        {
            inputHandler.ClearSelection();
        }
        else
        {
            ClearPlanetSelection();
            ClearStarSelection();
        }
    }
}