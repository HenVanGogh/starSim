using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Renderer))] // Ensure there's a Renderer to change material
public class StarSystemData : MonoBehaviour // Or just a plain C# class/struct if not a MonoBehaviour
{
    [Header("System Info")]
    public string systemName = "Unnamed System";

    [Header("Gameplay Data")]
    // List of directly connected neighbouring systems
    public List<StarSystemData> neighbours = new List<StarSystemData>();

    // --- Private Variables ---
    private Renderer rendererComponent;
    private Material originalMaterial;
    private SphereCollider sphereCollider; // Reference to the collider

    // Flags to track highlight state
    private bool isSelected = false;
    // REMOVED: private bool isNeighbourHighlighted = false; // Removed unused variable

    // Static reference to the manager for easy communication
    public static GalaxyGenerator galaxyManager;

    void Awake()
    {
        // Get essential components
        rendererComponent = GetComponent<Renderer>();
        originalMaterial = rendererComponent.material; // Store the default material

        // Ensure a SphereCollider exists for clicking
        sphereCollider = GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = gameObject.AddComponent<SphereCollider>();
            // Optional: Adjust collider radius based on the object's bounds
            sphereCollider.radius = rendererComponent.bounds.extents.magnitude;
        }
    }

    /// <summary>
    /// Initializes basic data for the star system. Called by GalaxyGenerator.
    /// </summary>
    public void Initialize(string name)
    {
        systemName = name;
        gameObject.name = name; // Set GameObject name for clarity in hierarchy
    }

    /// <summary>
    /// Called when the mouse button is pressed down while over this object's collider.
    /// </summary>
    void OnMouseDown()
    {
        // Notify the GalaxyGenerator that this star was selected
        if (galaxyManager != null)
        {
            galaxyManager.HandleStarSelection(this);
        }
        else
        {
            Debug.LogWarning("GalaxyManager reference not set in StarSystemData!");
        }
    }

    /// <summary>
    /// Applies the 'selected' highlight material.
    /// </summary>
    public void HighlightSelected()
    {
        if (galaxyManager != null && galaxyManager.selectedMaterial != null)
        {
            rendererComponent.material = galaxyManager.selectedMaterial;
            isSelected = true;
            // REMOVED: isNeighbourHighlighted = false; // Removed unused assignment
        }
    }

    /// <summary>
    /// Clears the list of known neighbours for this star system.
    /// Called by GalaxyGenerator when rebuilding connections.
    /// </summary>
    public void ClearNeighbours()
    {
        neighbours.Clear();
    }

    /// <summary>
    /// Applies the 'neighbour' highlight material, only if not already selected.
    /// </summary>
    public void HighlightNeighbour()
    {
        // Only highlight as neighbour if not the currently selected star
        if (!isSelected && galaxyManager != null && galaxyManager.neighbourMaterial != null)
        {
            rendererComponent.material = galaxyManager.neighbourMaterial;
            // REMOVED: isNeighbourHighlighted = true; // Removed unused assignment
        }
    }

    /// <summary>
    /// Reverts the material back to its original state.
    /// </summary>
    public void Unhighlight()
    {
        if (rendererComponent != null && originalMaterial != null)
        {
            rendererComponent.material = originalMaterial;
        }
        isSelected = false;
        // REMOVED: isNeighbourHighlighted = false; // Removed unused assignment
    }

    /// <summary>
    /// Adds a neighbour to the list if not already present.
    /// </summary>
    public void AddNeighbour(StarSystemData neighbour)
    {
        if (neighbour != null && !neighbours.Contains(neighbour))
        {
            neighbours.Add(neighbour);
        }
    }
}