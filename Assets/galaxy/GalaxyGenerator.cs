using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class GalaxyGenerator : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static GalaxyGenerator Instance { get; private set; }

    [Header("Generation Settings")]
    [Tooltip("Prefab used to represent a star system. MUST have a Renderer and ideally a StarSystemData component.")]
    public GameObject starPrefab;
    [Tooltip("Material used for the hyperlanes. Should support transparency.")]
    public Material hyperlaneMaterial;
    [Tooltip("Number of star systems to generate.")]
    public int numberOfStars = 100;
    [Tooltip("The approximate radius of the area on the XZ plane where stars will be generated.")]
    public float generationRadius = 50f;
    [Tooltip("Maximum variation in the Y-axis (height) for stars.")]
    public float yVariation = 5f;
    [Tooltip("Minimum distance desired between stars after spacing.")]
    public float minStarDistance = 5f;
    [Tooltip("How strongly stars push each other apart during spacing.")]
    public float repulsionStrength = 0.5f;
    [Tooltip("Number of iterations to run the spacing algorithm.")]
    public int spacingIterations = 30;
    [Tooltip("Maximum number of hyperlanes connected to a single star (includes MST connections).")]
    public int maxConnectionsPerStar = 4;
    [Tooltip("Maximum distance for *additional* hyperlane connections (MST connections can be longer).")]
    public float maxAdditionalConnectionDistance = 15f;

    [Header("Hyperlane Visuals")]
    public float hyperlaneStartWidth = 0.1f;
    public float hyperlaneEndWidth = 0.1f;
    public Color hyperlaneColor = new Color(0.5f, 0.5f, 1f, 0.5f);

    [Header("Highlighting")]
    [Tooltip("Material to apply to the selected star.")]
    public Material selectedMaterial;
    [Tooltip("Material to apply to neighbours of the selected star.")]
    public Material neighbourMaterial;

    [Header("External Controllers")] // Renamed Header for clarity
    [Tooltip("Assign a GameObject with a component that implements IVoronoiOverlayController.")]
    // --- USE INTERFACE TYPE ---
    public MonoBehaviour voronoiOverlayControllerComponent; // Assign in inspector (e.g., the GameObject holding VoronoiOverlayGenerator)
    private IVoronoiOverlayController voronoiOverlayController; // Internal reference to the interface


    // --- Private Variables ---
    private List<StarSystemData> starSystems = new List<StarSystemData>();
    // Graph representing connections (Star -> List of connected Stars)
    private Dictionary<StarSystemData, List<StarSystemData>> galaxyGraph = new Dictionary<StarSystemData, List<StarSystemData>>();
    // Keep track of generated connections (pairs) to avoid duplicates
    private HashSet<(StarSystemData, StarSystemData)> existingConnections = new HashSet<(StarSystemData, StarSystemData)>();
    private GameObject hyperlaneContainer; // Container for visual hyperlanes
    private StarSystemData currentlySelectedStar = null; // Track selected star for highlighting


    void Awake()
    {
        // --- Singleton Setup ---
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Duplicate GalaxyGenerator instance detected. Destroying self.", gameObject);
            Destroy(gameObject); // Destroy duplicate manager
        }
        else
        {
            Instance = this;
            // Optional: Make this instance persistent across scene loads if needed
            // DontDestroyOnLoad(gameObject);

            // --- GET INTERFACE REFERENCE ---
            if (voronoiOverlayControllerComponent != null)
            {
                // Try to get the interface implementation from the assigned component
                voronoiOverlayController = voronoiOverlayControllerComponent.GetComponent<IVoronoiOverlayController>();
                if (voronoiOverlayController == null)
                {
                    // Log an error if the assigned component doesn't implement the interface
                    Debug.LogError($"The component assigned to 'Voronoi Overlay Controller Component' on {gameObject.name} ('{voronoiOverlayControllerComponent.GetType().Name}') does not implement the IVoronoiOverlayController interface!", voronoiOverlayControllerComponent);
                } else {
                     Debug.Log($"Found IVoronoiOverlayController: {voronoiOverlayController.GetType().Name} on {voronoiOverlayControllerComponent.name}", this);
                }
            } else {
                 Debug.LogWarning("Voronoi Overlay Controller Component is not assigned in the inspector. Voronoi overlay will not be generated.", this);
            }

            // Set the static reference in StarSystemData IF StarSystemData needs it
            // Make sure StarSystemData actually has this static field and uses it appropriately.
             // StarSystemData.galaxyManager = this;
        }
    }


    void Start()
    {
        // --- Input Validation ---
        // Check essential prefabs and materials
        if (starPrefab == null) { Debug.LogError("Star Prefab is not assigned! Aborting generation.", this); return; }
        if (hyperlaneMaterial == null) { Debug.LogError("Hyperlane Material is not assigned! Aborting generation.", this); return; }
        if (selectedMaterial == null) { Debug.LogError("Selected Material is not assigned! Highlighting may fail.", this); /* Continue? */ }
        if (neighbourMaterial == null) { Debug.LogError("Neighbour Material is not assigned! Highlighting may fail.", this); /* Continue? */ }
        // Validate star prefab components
        if (starPrefab.GetComponent<Renderer>() == null) { Debug.LogError("Star Prefab MUST have a Renderer component! Aborting generation.", this); return; }
        if (starPrefab.GetComponent<Collider>() == null) { Debug.LogWarning("Star Prefab should ideally have a Collider component for selection.", this); }
        if (starPrefab.GetComponent<StarSystemData>() == null) { Debug.LogWarning("Star Prefab doesn't have StarSystemData component attached. It will be added automatically, but consider adding it to the prefab.", this); }


        // --- Generate Galaxy on Start ---
        GenerateFullGalaxy(); // Encapsulate all generation steps
    }

    /// <summary>
    /// Main method to generate or regenerate the entire galaxy.
    /// Clears existing data, generates stars, spaces them, connects them, and triggers Voronoi overlay.
    /// </summary>
    [ContextMenu("Regenerate Full Galaxy")] // Allow triggering from Inspector context menu
    public void GenerateFullGalaxy()
    {
         Debug.Log("--- Starting Full Galaxy Generation ---");
         ClearGalaxy(); // Clear previous galaxy state first
         GenerateInitialStars();
         SpaceOutStars();
         ConnectStarsWithHyperlanes();
         TriggerVoronoiGeneration(); // Use the new trigger method via interface
         Debug.Log("--- Full Galaxy Generation Complete ---");
    }

    /// <summary>
    /// Clears all generated galaxy objects (stars, hyperlanes) and associated data structures.
    /// Also clears the Voronoi overlay via the controller interface.
    /// </summary>
    void ClearGalaxy()
    {
        Debug.Log("Clearing existing galaxy objects and data...");

         // Destroy existing star GameObjects
         foreach (var star in starSystems)
         {
             if (star != null && star.gameObject != null) {
                 // Use DestroyImmediateSafe if needed for editor functionality outside play mode
                 Destroy(star.gameObject);
             }
         }
         // Destroy hyperlane container GameObject
         if (hyperlaneContainer != null)
         {
             Destroy(hyperlaneContainer);
             hyperlaneContainer = null; // Nullify reference after destruction
         }

        // Clear data structures
        starSystems.Clear();
        galaxyGraph.Clear();
        existingConnections.Clear();
        currentlySelectedStar = null;

        // Clear the Voronoi overlay using the interface (if available)
        if (voronoiOverlayController != null) {
             voronoiOverlayController.ClearOverlay();
             Debug.Log("Voronoi overlay cleared via controller.");
        } else {
             Debug.Log("Voronoi controller not available, skipping overlay clear.");
        }
        Debug.Log("Galaxy clearing complete.");
    }


    /// <summary>
    /// Generates the initial random placement of star systems.
    /// </summary>
    void GenerateInitialStars()
    {
        // Ensure prefab is valid before starting loop
        if (starPrefab == null) {
             Debug.LogError("Cannot generate stars: Star Prefab is null.", this);
             return;
        }

        // --- Generation Loop ---
        for (int i = 0; i < numberOfStars; i++)
        {
            // Generate position using polar coordinates for circular distribution
            float angle = Random.Range(0f, Mathf.PI * 2f);
            // Use square root for more even distribution within the circle
            float distance = Mathf.Sqrt(Random.Range(0f, 1f)) * generationRadius;
            float x = distance * Mathf.Cos(angle);
            float z = distance * Mathf.Sin(angle);
            float y = Random.Range(-yVariation / 2f, yVariation / 2f); // Random height
            Vector3 position = new Vector3(x, y, z);

            // Instantiate and Setup Star GameObject
            GameObject starGO = Instantiate(starPrefab, position, Quaternion.identity, transform); // Parent to this generator object
            StarSystemData starData = starGO.GetComponent<StarSystemData>();
            if (starData == null) // Add component if prefab doesn't have it (log warning in Start)
            {
                starData = starGO.AddComponent<StarSystemData>();
            }
            // Initialize the star data (name, potentially linking back to manager if needed)
            starData.Initialize("Star_" + i); // Pass initial name

            // Store references
            starSystems.Add(starData);
            galaxyGraph.Add(starData, new List<StarSystemData>()); // Initialize graph entry
        }
        Debug.Log($"Generated initial positions for {starSystems.Count} stars.");
    }

    /// <summary>
    /// Iteratively adjusts star positions to enforce minimum distance using a simple repulsion model.
    /// </summary>
    void SpaceOutStars()
    {
        // Skip if spacing is disabled or parameters are invalid
        if (spacingIterations <= 0 || minStarDistance <= 0 || starSystems.Count < 2) {
            Debug.Log("Skipping star spacing (iterations=0, minDistance<=0, or <2 stars).");
            return;
        }

        Debug.Log($"Starting star spacing ({spacingIterations} iterations, min dist: {minStarDistance})...");
        // Pre-allocate force list for efficiency
        List<Vector3> forces = new List<Vector3>(Enumerable.Repeat(Vector3.zero, starSystems.Count));

        float minDistanceSqr = minStarDistance * minStarDistance; // Use squared distance

        // Spacing Iteration Loop
        for (int iter = 0; iter < spacingIterations; iter++)
        {
            // Reset forces for this iteration
            for (int f = 0; f < forces.Count; f++) forces[f] = Vector3.zero;

            // Calculate pairwise repulsion forces
            for (int i = 0; i < starSystems.Count; i++)
            {
                for (int j = i + 1; j < starSystems.Count; j++) // Compare each pair only once
                {
                    StarSystemData starA = starSystems[i];
                    StarSystemData starB = starSystems[j];

                    // Ensure objects haven't been destroyed somehow
                    if (starA == null || starB == null) continue;

                    Vector3 direction = starA.transform.position - starB.transform.position;
                    float distanceSqr = direction.sqrMagnitude;

                    // Apply force only if stars are too close and not exactly coincident
                    if (distanceSqr < minDistanceSqr && distanceSqr > 0.0001f)
                    {
                        float distance = Mathf.Sqrt(distanceSqr);
                        // Force magnitude increases as distance decreases below minimum
                        // Avoid division by zero if distance is extremely small
                        float forceMagnitude = repulsionStrength * (minStarDistance - distance) / (distance + 0.001f);
                        Vector3 force = direction.normalized * forceMagnitude;

                        // Apply equal and opposite forces
                        forces[i] += force;
                        forces[j] -= force;
                    }
                }
            }

            // Apply the calculated forces to update star positions
            for(int i=0; i< starSystems.Count; i++)
            {
                StarSystemData star = starSystems[i];
                if (star == null) continue; // Check again in case of issues

                Vector3 currentPos = star.transform.position;
                // Apply force gently - adjust the multiplier (e.g., Time.deltaTime if running over frames)
                Vector3 displacement = forces[i] * 0.1f; // Apply a fraction of the force per iteration
                Vector3 newPos = currentPos + displacement;

                // --- Optional: Clamp position to stay within generation radius/height ---
                Vector2 xzPos = new Vector2(newPos.x, newPos.z);
                if (xzPos.magnitude > generationRadius)
                {
                    // If outside radius, pull back onto the edge
                    xzPos = xzPos.normalized * generationRadius;
                    newPos = new Vector3(xzPos.x, Mathf.Clamp(newPos.y, -yVariation / 2f, yVariation / 2f), xzPos.y);
                } else {
                    // If inside radius, just clamp height
                     newPos.y = Mathf.Clamp(newPos.y, -yVariation / 2f, yVariation / 2f);
                }
                // --- End Optional Clamping ---

                star.transform.position = newPos;
            }
        } // End Spacing Iteration Loop
        Debug.Log("Finished star spacing.");
    }


    /// <summary>
    /// Connects stars with hyperlanes. Ensures full connectivity via Minimum Spanning Tree (MST),
    /// then adds additional shorter connections up to `maxConnectionsPerStar`.
    /// </summary>
    void ConnectStarsWithHyperlanes()
    {
        if (starSystems.Count < 2) {
             Debug.Log("Skipping hyperlane generation: Need at least 2 stars.");
             return;
        }

        // Create or clear container for hyperlane visuals
        if (hyperlaneContainer != null) Destroy(hyperlaneContainer);
        hyperlaneContainer = new GameObject("Hyperlanes");
        hyperlaneContainer.transform.SetParent(transform); // Parent to this generator
        hyperlaneContainer.transform.localPosition = Vector3.zero;

        existingConnections.Clear(); // Clear tracking before rebuilding
        // Clear existing graph connections and neighbour lists in StarSystemData
        foreach (var star in starSystems) {
             if (galaxyGraph.ContainsKey(star)) galaxyGraph[star].Clear();
             else galaxyGraph.Add(star, new List<StarSystemData>()); // Ensure entry exists
             star?.ClearNeighbours(); // Assuming StarSystemData has a ClearNeighbours method
        }


        // --- 1. Build Minimum Spanning Tree (MST) using Prim's Algorithm ---
        // Ensures all stars are connected with the shortest possible total lane length.
        HashSet<StarSystemData> visited = new HashSet<StarSystemData>(); // Nodes included in the MST
        // Priority queue based on distance (use List + Sort for simplicity here)
        List<(float distance, StarSystemData starA, StarSystemData starB)> edgeCandidates =
            new List<(float, StarSystemData, StarSystemData)>();

        // Start Prim's from the first star
        StarSystemData startNode = starSystems[0];
        visited.Add(startNode);
        AddPotentialEdges(startNode, visited, edgeCandidates); // Add edges from start node

        Debug.Log("Building Minimum Spanning Tree (MST) for hyperlanes...");
        while (visited.Count < starSystems.Count && edgeCandidates.Count > 0)
        {
            // Find the shortest edge connecting a visited node to an unvisited node
            edgeCandidates.Sort((a, b) => a.distance.CompareTo(b.distance)); // Sort ascending by distance
            var bestEdge = edgeCandidates[0];
            edgeCandidates.RemoveAt(0); // Consume the shortest edge

            StarSystemData nodeToAdd = null;
            // Check which end of the edge is not yet visited
            if (!visited.Contains(bestEdge.starA)) nodeToAdd = bestEdge.starA;
            else if (!visited.Contains(bestEdge.starB)) nodeToAdd = bestEdge.starB;

            // If we found an unvisited node connected by this edge, add it to the MST
            if (nodeToAdd != null)
            {
                visited.Add(nodeToAdd); // Mark as visited
                AddConnection(bestEdge.starA, bestEdge.starB); // Add the connection (graph, visuals)
                AddPotentialEdges(nodeToAdd, visited, edgeCandidates); // Add new candidate edges from this node
            }
            // Else: both ends were already visited, discard this edge (it would form a cycle)
        }
        Debug.Log($"MST built. Connected {visited.Count}/{starSystems.Count} stars with {existingConnections.Count} essential hyperlanes.");
        if (visited.Count < starSystems.Count) {
            Debug.LogWarning("MST generation failed to connect all stars. Check for isolated components.");
        }


        // --- 2. Add Additional Short Connections ---
        // Adds more connections for gameplay variety, prioritizing shorter distances,
        // up to the maxConnectionsPerStar limit.
        Debug.Log($"Adding additional hyperlanes (max dist: {maxAdditionalConnectionDistance}, max conn/star: {maxConnectionsPerStar})...");
        int additionalConnections = 0;
        // Create a list of all possible pairs within the max distance
        List<(float distance, StarSystemData starA, StarSystemData starB)> allPossiblePairs =
             new List<(float, StarSystemData, StarSystemData)>();

        for (int i = 0; i < starSystems.Count; i++)
        {
            for (int j = i + 1; j < starSystems.Count; j++) // Avoid duplicates and self-connections
            {
                StarSystemData starA = starSystems[i];
                StarSystemData starB = starSystems[j];
                 if (starA == null || starB == null) continue; // Safety check

                float dist = Vector3.Distance(starA.transform.position, starB.transform.position);
                if (dist <= maxAdditionalConnectionDistance) // Check distance limit
                {
                    allPossiblePairs.Add((dist, starA, starB));
                }
            }
        }

        // Sort potential connections by distance (shortest first)
        allPossiblePairs.Sort((a, b) => a.distance.CompareTo(b.distance));

        // Iterate through sorted pairs and add connections if limits allow
        foreach (var pair in allPossiblePairs)
        {
            StarSystemData starA = pair.starA;
            StarSystemData starB = pair.starB;

            // Check:
            // 1. If connection doesn't already exist (from MST or previous additions).
            // 2. If both stars are below their max connection limit.
            if (!ConnectionExists(starA, starB) &&
                galaxyGraph[starA].Count < maxConnectionsPerStar &&
                galaxyGraph[starB].Count < maxConnectionsPerStar)
            {
                AddConnection(starA, starB); // Add the connection
                additionalConnections++;
            }
        }
        Debug.Log($"Added {additionalConnections} additional hyperlanes. Total hyperlanes: {existingConnections.Count}");
    }

    /// <summary>
    /// Helper for Prim's MST: Adds potential edges from a newly visited node to all unvisited nodes.
    /// </summary>
    void AddPotentialEdges(StarSystemData node, HashSet<StarSystemData> visited, List<(float distance, StarSystemData starA, StarSystemData starB)> edgeCandidates)
    {
        if (node == null) return;
        foreach (StarSystemData otherNode in starSystems)
        {
             if (otherNode == null || node == otherNode) continue; // Skip self and nulls
            // Add edge only if the other node hasn't been visited yet
            if (!visited.Contains(otherNode))
            {
                float dist = Vector3.Distance(node.transform.position, otherNode.transform.position);
                edgeCandidates.Add((dist, node, otherNode));
            }
        }
    }

    /// <summary>
    /// Checks if a connection between two stars already exists in the `existingConnections` set.
    /// Ensures order doesn't matter by using InstanceID comparison.
    /// </summary>
    bool ConnectionExists(StarSystemData starA, StarSystemData starB)
    {
         if (starA == null || starB == null) return true; // Prevent connecting nulls
        // Create a canonical pair based on InstanceID to ensure order doesn't matter
        var pair = starA.GetInstanceID() < starB.GetInstanceID() ? (starA, starB) : (starB, starA);
        return existingConnections.Contains(pair);
    }

    /// <summary>
    /// Adds a connection between two stars: updates the graph, neighbour lists, tracking set, and creates the visual LineRenderer.
    /// </summary>
    void AddConnection(StarSystemData starA, StarSystemData starB)
    {
         if (starA == null || starB == null) {
              Debug.LogWarning("Attempted to add connection involving a null star.", this);
              return;
         }
        // Create canonical pair
        var pair = starA.GetInstanceID() < starB.GetInstanceID() ? (starA, starB) : (starB, starA);

        // Add only if it doesn't exist yet
        if (existingConnections.Add(pair)) // .Add returns true if the item was new
        {
            // Update Graph (ensure keys exist)
            if (!galaxyGraph.ContainsKey(starA)) galaxyGraph.Add(starA, new List<StarSystemData>());
            if (!galaxyGraph.ContainsKey(starB)) galaxyGraph.Add(starB, new List<StarSystemData>());
            galaxyGraph[starA].Add(starB);
            galaxyGraph[starB].Add(starA);

            // Update Neighbour lists in StarSystemData (assuming these methods exist)
            starA.AddNeighbour(starB);
            starB.AddNeighbour(starA);

            // Create the visual representation
            CreateHyperlaneVisual(starA, starB, hyperlaneContainer.transform);
        }
    }


    /// <summary>
    /// Creates the visual LineRenderer for a hyperlane between two stars.
    /// </summary>
    void CreateHyperlaneVisual(StarSystemData starA, StarSystemData starB, Transform parent)
    {
         if (starA == null || starB == null || hyperlaneMaterial == null) return; // Safety checks

        GameObject hyperlaneObject = new GameObject($"Hyperlane_{starA.systemName}-{starB.systemName}");
        hyperlaneObject.transform.SetParent(parent); // Parent to the hyperlane container
        hyperlaneObject.transform.localPosition = Vector3.zero; // Position relative to parent

        LineRenderer lineRenderer = hyperlaneObject.AddComponent<LineRenderer>();

        // Set positions
        lineRenderer.positionCount = 2;
        // Use star positions directly (LineRenderer is in world space by default)
        lineRenderer.SetPosition(0, starA.transform.position);
        lineRenderer.SetPosition(1, starB.transform.position);

        // Set visual properties
        lineRenderer.material = hyperlaneMaterial;
        lineRenderer.startWidth = hyperlaneStartWidth;
        lineRenderer.endWidth = hyperlaneEndWidth;
        lineRenderer.startColor = hyperlaneColor;
        lineRenderer.endColor = hyperlaneColor;

        // Optional: Disable shadows, configure alignment, etc.
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.alignment = LineAlignment.View; // Or TransformZ depending on desired look
    }

    /// <summary>
    /// Handles the logic when a star system is clicked/selected.
    /// Called by StarSystemData's selection mechanism (e.g., OnMouseDown).
    /// Manages highlighting of the selected star and its neighbours.
    /// </summary>
    /// <param name="selectedStar">The StarSystemData component of the clicked star.</param>
    public void HandleStarSelection(StarSystemData selectedStar)
    {
        if (selectedStar == null) return; // Clicked on something else?

        // --- Unhighlight Previous Selection (if any) ---
        if (currentlySelectedStar != null && currentlySelectedStar != selectedStar) // Check it's not the same star
        {
            // Unhighlight the previously selected star itself (assuming Unhighlight exists)
            currentlySelectedStar.Unhighlight();

            // Unhighlight all neighbours of the previously selected star
            if (galaxyGraph.TryGetValue(currentlySelectedStar, out var oldNeighbours)) {
                 foreach (StarSystemData neighbour in oldNeighbours)
                 {
                     // Only unhighlight if it's not the newly selected star or one of its new neighbours
                     if (neighbour != null && neighbour != selectedStar &&
                         (!galaxyGraph.ContainsKey(selectedStar) || !galaxyGraph[selectedStar].Contains(neighbour)))
                     {
                         neighbour.Unhighlight();
                     }
                 }
            }
        }

        // --- Highlight New Selection ---
        // Highlight the newly selected star (assuming HighlightSelected exists)
        selectedStar.HighlightSelected();

        // Highlight all direct neighbours of the newly selected star
        if (galaxyGraph.TryGetValue(selectedStar, out var newNeighbours)) {
             foreach (StarSystemData neighbour in newNeighbours)
             {
                 if (neighbour != null) {
                      // Highlight neighbour (assuming HighlightNeighbour exists and handles not overwriting selected)
                      neighbour.HighlightNeighbour();
                 }
             }
        }

        // Update the currently selected star reference
        currentlySelectedStar = selectedStar;

        Debug.Log($"Selected Star: {selectedStar.systemName}");
        // Optional: Display neighbour info, pathfind, etc.
    }


    /// <summary>
    /// Triggers the generation of the Voronoi overlay using the assigned controller interface.
    /// Passes the current list of generated star systems to the controller.
    /// </summary>
    void TriggerVoronoiGeneration()
    {
        // Use the IVoronoiOverlayController interface reference obtained in Awake
        if (starSystems != null && starSystems.Count > 0)
        {
            voronoiOverlayController.ApplyQueuedMergesAndRegenerate(starSystems);
            Debug.Log("Disabling all Voronoi zones...");
            foreach (var star in starSystems)
            {
                voronoiOverlayController.DisableZone(star);
            }
            Debug.Log("All Voronoi zones disabled.");

            // Pick one star system at random
            if (starSystems.Count > 0)
            {
                int randomIndex = Random.Range(0, starSystems.Count);
                StarSystemData centralStar = starSystems[randomIndex];
                Debug.Log($"Selected central star: {centralStar.name}");

                if (galaxyGraph.ContainsKey(centralStar))
                {
                    List<StarSystemData> neighbors = galaxyGraph[centralStar];

                    // Take up to 10 neighbors
                    List<StarSystemData> neighborsToMerge = neighbors.Take(10).ToList();
                    Debug.Log($"Found {neighbors.Count} neighbors. Queuing merges for {neighborsToMerge.Count} neighbors.");

                    // Queue merge requests
                    foreach (var neighbor in neighborsToMerge)
                    {
                        if (neighbor != centralStar) // Avoid merging with itself
                        {
                            voronoiOverlayController.QueueMergeRequest(centralStar, neighbor);
                            Debug.Log($"Queued merge: {neighbor.name} -> {centralStar.name}");
                        }
                    }

                    // Trigger Voronoi overlay regeneration to apply the merges
                    Debug.Log("Triggering Voronoi Overlay generation to apply merges...");
                     // Regenerate with all systems
                    Debug.Log("Voronoi generation triggered after merges.");

                    // Enable the central star and the merged neighbors
                    Debug.Log("Enabling the central star and merged neighbors...");
                    voronoiOverlayController.EnableZone(centralStar); // Assuming you have an EnableZone function
                    foreach (var mergedNeighbor in neighborsToMerge)
                    {
                        voronoiOverlayController.EnableZone(mergedNeighbor); // Assuming you have an EnableZone function
                    }
                    Debug.Log("Central star and merged neighbors enabled.");
                }
                else
                {
                    Debug.Log($"Central star '{centralStar.name}' has no connections in the galaxy graph.");
                }
            }
            else
            {
                Debug.LogWarning("No star systems available to pick a central star.");
            }
        }
        else
        {
            // This message now indicates the interface wasn't found or assigned correctly in Awake
            Debug.Log("IVoronoiOverlayController is not available (check assignment in inspector). Skipping overlay generation.");
        }
    }

     // --- Example: How you might use merging via the controller ---
     /// <summary>
     /// Example function to queue a merge between two random adjacent stars and regenerate the overlay.
     /// Can be triggered from UI Button, Inspector Context Menu, etc.
     /// </summary>
     [ContextMenu("Merge Two Random Neighbours & Regenerate")]
     public void MergeTwoRandomNeighboursAndRegenerate() {
         if (voronoiOverlayController == null) {
             Debug.LogError("Cannot merge: Voronoi controller not found.", this);
             return;
         }
         if (starSystems.Count < 2) {
             Debug.LogWarning("Cannot merge: Need at least two stars.", this);
             return;
         }

         // Find a star that has neighbours in the graph
         List<StarSystemData> potentialTargets = starSystems
             .Where(s => s != null && galaxyGraph.ContainsKey(s) && galaxyGraph[s].Count > 0)
             .ToList();

         if (potentialTargets.Count == 0) {
             Debug.LogWarning("Cannot merge: No stars with hyperlane neighbours found.", this);
             return;
         }
         // Pick a random star from the potential targets
         StarSystemData starToMergeInto = potentialTargets[Random.Range(0, potentialTargets.Count)];

         // Pick a random neighbour of the chosen star to merge *from*
         List<StarSystemData> neighbours = galaxyGraph[starToMergeInto].Where(n => n != null).ToList();
         if (neighbours.Count == 0) {
              Debug.LogWarning($"Cannot merge: Star '{starToMergeInto.name}' has no valid neighbours in the graph.", this);
              return;
         }
         StarSystemData starToMergeFrom = neighbours[Random.Range(0, neighbours.Count)];

         Debug.Log($"Attempting to queue merge: {starToMergeFrom.name} into {starToMergeInto.name}");

         // --- Queue the merge request via the interface ---
         bool queued = voronoiOverlayController.QueueMergeRequest(starToMergeInto, starToMergeFrom);

         if (queued) {
             // --- IMPORTANT: Apply merges and regenerate the overlay ---
             // The overlay visuals don't update until this is called.
             Debug.Log("Merge queued successfully. Regenerating Voronoi overlay to apply changes...");
             // Pass the current list of *all* stars. The controller handles filtering based on the queue.
             voronoiOverlayController.ApplyQueuedMergesAndRegenerate(starSystems);
             Debug.Log("Voronoi overlay regeneration triggered after queuing merge.");
         } else {
             Debug.LogError($"Failed to queue merge request between {starToMergeFrom.name} and {starToMergeInto.name}.", this);
         }
     }
}