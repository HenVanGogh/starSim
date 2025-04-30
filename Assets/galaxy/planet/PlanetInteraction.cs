using UnityEngine;
using System;

/// <summary>
/// Handles interactions with individual planets in a star system.
/// Attached to each planet during solar system generation.
/// </summary>
public class PlanetInteraction : MonoBehaviour, IClickableObject
{
    // Basic planet information
    [Header("Planet Data")]
    public string planetName = "Unnamed Planet";
    public int planetIndex;  // Index in the star system
    
    // Planet statistics/resources
    [Header("Planet Properties")]
    [SerializeField] private float habitability = 0f;  // 0-1 scale: 0 = uninhabitable, 1 = perfect
    [SerializeField] private float resources = 0f;      // 0-1 scale: 0 = barren, 1 = abundant
    [SerializeField] private float size = 1f;           // 1 = earth-sized
    
    // Planet status
    [Header("Status")]
    [SerializeField] private bool _isSelected = false;
    public bool isColonized = false;
    public bool isExplored = false;
    
    // Implementation of IClickableObject interface
    public bool isSelected 
    { 
        get { return _isSelected; }
        set { 
            _isSelected = value;
            UpdateVisualState();
        }
    }
    
    // Visual feedback for interactions
    [Header("Visual Feedback")]
    public Material defaultMaterial;
    public Material highlightMaterial;
    public Material selectedMaterial;
    private Renderer planetRenderer;
    
    // Reference to the InputHandler
    private InputHandler inputHandler;
    
    private void Awake()
    {
        // Cache the renderer
        planetRenderer = GetComponent<Renderer>();
        if (planetRenderer == null)
        {
            Debug.LogWarning("No renderer found on planet object!", this);
        }
        
        // Store the default material if not assigned
        if (defaultMaterial == null && planetRenderer != null)
        {
            defaultMaterial = planetRenderer.material;
        }
        
        // Make sure we have a collider for interactions
        if (GetComponent<Collider>() == null)
        {
            // Add a sphere collider if none exists
            SphereCollider collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = size * 0.5f; // Half the size as radius
        }
        
        // Find the input handler
        inputHandler = FindAnyObjectByType<InputHandler>();
        if (inputHandler == null)
        {
            Debug.LogWarning("InputHandler not found in the scene!", this);
        }
    }
    
    private void Start()
    {
        // Generate planet properties if they're at default values
        if (habitability == 0 && resources == 0)
        {
            GenerateRandomProperties();
        }
    }
    
    /// <summary>
    /// Generates random properties for this planet
    /// </summary>
    public void GenerateRandomProperties()
    {
        // Generate a semi-random planet name if not set
        if (planetName == "Unnamed Planet")
        {
            planetName = "Planet " + planetIndex;
        }
        
        // Generate random planet properties
        habitability = Mathf.Clamp01(UnityEngine.Random.value * UnityEngine.Random.value); // Squared to make habitable planets rarer
        resources = Mathf.Clamp01(UnityEngine.Random.value);
        size = UnityEngine.Random.Range(0.5f, 2.0f);
        
        // Update the planet's scale based on size
        transform.localScale = Vector3.one * size;
        
        // Update the collider size
        SphereCollider collider = GetComponent<SphereCollider>();
        if (collider != null)
        {
            collider.radius = 0.5f; // Keep radius at half-size since we scaled the transform
        }
    }
    
    /// <summary>
    /// Called when planet is clicked
    /// </summary>
    private void OnMouseDown()
    {
        // Notify the input handler about the click
        if (inputHandler != null)
        {
            inputHandler.HandleObjectClicked(this);
        }
        else
        {
            // Fallback behavior if no input handler is found
            ToggleSelection();
            Debug.Log($"Planet {planetName} clicked! Habitability: {habitability:F2}, Resources: {resources:F2}");
        }
    }
    
    /// <summary>
    /// Handle hover enter
    /// </summary>
    private void OnMouseEnter()
    {
        // Highlight the planet
        if (!isSelected && planetRenderer != null && highlightMaterial != null)
        {
            planetRenderer.material = highlightMaterial;
        }
        
        // Notify input handler
        if (inputHandler != null)
        {
            inputHandler.HandleObjectHoverEnter(this);
        }
    }
    
    /// <summary>
    /// Handle hover exit
    /// </summary>
    private void OnMouseExit()
    {
        // Remove highlight if not selected
        if (!isSelected)
        {
            RestoreDefaultMaterial();
        }
        
        // Notify input handler
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
        return ClickableObjectType.Planet;
    }
    
    /// <summary>
    /// Toggle the selection state of this planet
    /// </summary>
    public void ToggleSelection()
    {
        isSelected = !isSelected;
        UpdateVisualState();
    }
    
    /// <summary>
    /// Updates the visual state based on selection status
    /// </summary>
    private void UpdateVisualState()
    {
        if (planetRenderer == null) return;
        
        if (isSelected && selectedMaterial != null)
        {
            planetRenderer.material = selectedMaterial;
        }
        else
        {
            RestoreDefaultMaterial();
        }
    }
    
    /// <summary>
    /// Restores the default material
    /// </summary>
    private void RestoreDefaultMaterial()
    {
        if (planetRenderer != null && defaultMaterial != null)
        {
            planetRenderer.material = defaultMaterial;
        }
    }
    
    /// <summary>
    /// Sets the selected state with optional visual update
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
    /// Gets the planet's data as a formatted string
    /// </summary>
    public string GetPlanetInfoText()
    {
        string status = isExplored ? (isColonized ? "Colonized" : "Explored") : "Unexplored";
        return $"Planet: {planetName}\n" +
               $"Size: {(size >= 1.5f ? "Large" : (size <= 0.7f ? "Small" : "Medium"))}\n" +
               $"Habitability: {habitability:P0}\n" +
               $"Resources: {resources:P0}\n" +
               $"Status: {status}";
    }
    
    // Getters for properties
    public float GetHabitability() => habitability;
    public float GetResources() => resources;
    public float GetSize() => size;
    
    // Setters with validation
    public void SetHabitability(float value) => habitability = Mathf.Clamp01(value);
    public void SetResources(float value) => resources = Mathf.Clamp01(value);
    
    /// <summary>
    /// Set the planet as explored
    /// </summary>
    public void Explore()
    {
        if (!isExplored)
        {
            isExplored = true;
            Debug.Log($"Planet {planetName} has been explored!");
        }
    }
    
    /// <summary>
    /// Set the planet as colonized
    /// </summary>
    public void Colonize()
    {
        if (!isColonized)
        {
            isExplored = true; // Colonizing implies exploring
            isColonized = true;
            Debug.Log($"Planet {planetName} has been colonized!");
        }
    }
}