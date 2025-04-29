#region Using Statements
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using DelaunatorSharp; // Required for Delaunay triangulation
#endregion

// --- !!! IMPORTANT !!! ---
// This script REQUIRES the 'Delaunator' NuGet package installed via NuGetForUnity.
// This version uses a CIRCULAR ring of "Fake Points", adds functionality
// to MERGE specific adjacent Voronoi cells, and assigns UNIQUE RANDOMIZED MATERIALS
// to each final cell/region before generating meshes.
// It includes:
// 1. Calculation of max radius of real stars.
// 2. Generation of fake points on a circle.
// 3. Adjacency calculation between real cells based on Delaunay triangulation.
// 4. Polygon merging logic for adjacent cells (both internal test and external queue).
// 5. Filtering during mesh generation.
// 6. Ear Clipping triangulation & polygon cleaning.
// 7. Material instantiation and random color assignment per region.
// 8. Targeted merging test based on neighbors (optional).
// 9. Implements IVoronoiOverlayController for external control.

// Implement the IVoronoiOverlayController interface
public class VoronoiOverlayGenerator : MonoBehaviour, IVoronoiOverlayController
{
    #region Public Inspector Fields
    [Header("References")]
    [Tooltip("The base material to use for cell fills. A unique instance with a random color will be created for each region.")]
    public Material cellFillMaterialTemplate;
    public Material cellBorderMaterial;

    [Header("Visual Settings")]
    public float yOffset = -0.1f; // Offset below the star plane
    public float borderWidth = 0.1f; // Width of the border mesh
    [Tooltip("The name of the color property in the cell fill material's shader (e.g., '_BaseColor' for URP/HDRP Lit, '_Color' for Standard).")]
    public string fillColorPropertyName = "_BaseColor";

    [Header("Generation Settings")]
    [Tooltip("How far outside the furthest real star to place the fake points.")]
    public float fakePointOffset = 50.0f;
    [Tooltip("Number of fake points to distribute around the circle.")]
    [Range(8, 64)] public int numFakePoints = 24;
    [Tooltip("Bounds used ONLY for circumcenter sanity checks.")]
    public Rect circumcenterCheckBounds = new Rect(-150, -150, 300, 300);

    [Header("Merging Test Settings")]
    [Tooltip("Enable the specific internal merging test (runs *after* queued merges).")]
    public bool enableSpecificMergeTest = false; // Defaulting to false as external control is preferred
    [Tooltip("Number of central stars to pick for the merge test.")]
    [Range(1, 10)] public int mergeTestCentralStars = 3;
    [Tooltip("Maximum number of neighbors to merge into each central star.")]
    [Range(1, 10)] public int mergeTestNeighborsToMerge = 5;

    [Header("Debugging")]
    [SerializeField] private bool enableVerboseLogging = false;
    [SerializeField] private int maxLoopIterationsPerSite = 1000;
    #endregion

    #region Private Variables
    private GameObject overlayContainer;
    // Map StarSystemData to the generated fill and border GameObjects
    private Dictionary<StarSystemData, GameObject> starToFillObjectMap = new Dictionary<StarSystemData, GameObject>();
    private Dictionary<StarSystemData, GameObject> starToBorderObjectMap = new Dictionary<StarSystemData, GameObject>();
    // Keep the generic list for easy cleanup, but use maps for control
    private List<GameObject> generatedOverlayObjects = new List<GameObject>();
    private const float Epsilon = 1e-6f;
    private float calculatedFakePointRadius = 0f;

    public bool enableSmoothing = true; // Set to true to enable Chaikin smoothing
    public int chaikinIterations = 3; // Adjust smoothness (2-4 is usually good)

    // Stores adjacency list for FINAL REAL sites only: site -> list of adjacent real sites
    private Dictionary<Vector2, List<Vector2>> realSiteAdjacency = new Dictionary<Vector2, List<Vector2>>();
    // Maps site index (in the combined list of ALL points) to its Vector2 position
    private Dictionary<int, Vector2> siteIndexToPos = new Dictionary<int, Vector2>();
    // Maps site position back to its index (in the combined list of ALL points)
    private Dictionary<Vector2, int> sitePosToIndex = new Dictionary<Vector2, int>();
    // Stores the calculated circumcenters for the current triangulation
    private Vector2?[] calculatedCircumcenters;
    // Stores the Delaunator instance for the current generation
    private Delaunator currentDelaunator;

    // Stores the unique material instance for each final region (keyed by the region's site Vector2)
    private Dictionary<Vector2, Material> regionMaterials = new Dictionary<Vector2, Material>();
    // Map site position (Vector2) back to the original StarSystemData (for the final, post-merge sites)
    private Dictionary<Vector2, StarSystemData> siteToStarDataMap = new Dictionary<Vector2, StarSystemData>();
    // Map StarSystemData back to its final site position (Vector2)
    private Dictionary<StarSystemData, Vector2> starDataToSiteMap = new Dictionary<StarSystemData, Vector2>();
    // Store the last used list of stars for regeneration without new merges
    private List<StarSystemData> lastUsedStarSystems;
    // Store queued merge requests from external controllers
    private List<(StarSystemData target, StarSystemData source)> queuedMerges = new List<(StarSystemData target, StarSystemData source)>();

    // Stores messages that have been logged once to prevent spam
    private static readonly HashSet<string> loggedWarnings = new HashSet<string>();
    #endregion

    #region Unity Lifecycle Methods
    void Start()
    {
        // Validation remains important
        if (cellFillMaterialTemplate == null) { Debug.LogError("Voronoi Overlay: Cell Fill Material Template is not assigned!", this); enabled = false; return; }
        if (cellBorderMaterial == null) { Debug.LogError("Voronoi Overlay: Cell Border Material is not assigned!", this); enabled = false; return; }
        if (string.IsNullOrEmpty(fillColorPropertyName)) { LogWarningOnceStatic("Voronoi Overlay: Fill Color Property Name is empty. Material color might not be set correctly.", this); }

        // --- REMOVE GENERATION CALL FROM START ---
        // Generation is now triggered externally via the IVoronoiOverlayController interface.
    }

    // Called when the script is disabled or destroyed
    void OnDisable() {
         // Clear static warnings set when disabling/recompiling in editor
         #if UNITY_EDITOR
         if (!Application.isPlaying) {
             loggedWarnings.Clear();
         }
         #endif
    }

    void OnDestroy() {
         // Clear static warnings set when stopping play mode
         loggedWarnings.Clear();
         // Ensure materials are cleaned up if the object is destroyed
         ClearOverlay(); // ClearOverlay handles material destruction
    }
    #endregion


    /// <summary>
    /// The minimum distance vertices must be apart to be kept during filtering.
    /// Points closer than this to their predecessor will be removed.
    /// Adjust based on your coordinate scale. Set this in the Unity Inspector or in code.
    /// </summary>
    [Tooltip("Minimum distance between vertices in the final polygon outline.")]
    public float vertexDistanceThreshold = 0.1f;

    #region Public API (Implementing IVoronoiOverlayController and ClearOverlay)

    // Public method to clear everything related to the overlay
    public void ClearOverlay()
    {
        if (overlayContainer != null)
        {
            // Safe destroy for editor/play mode
            if (Application.isPlaying) { Destroy(overlayContainer); }
            else { DestroyImmediate(overlayContainer); }
        }
        // Clear all tracking collections
        starToFillObjectMap.Clear();
        starToBorderObjectMap.Clear();
        generatedOverlayObjects.Clear(); // Contains references to GOs, now cleared by destroying container
        realSiteAdjacency.Clear();
        siteIndexToPos.Clear();
        sitePosToIndex.Clear();
        siteToStarDataMap.Clear();
        starDataToSiteMap.Clear();
        queuedMerges.Clear(); // Clear pending merges
        lastUsedStarSystems = null; // Clear last used stars

        overlayContainer = null;
        calculatedFakePointRadius = 0f;
        calculatedCircumcenters = null;
        currentDelaunator = null;

        // Clean up generated materials
        foreach (var mat in regionMaterials.Values)
        {
            if (mat != null) { DestroyImmediateSafe(mat); }
        }
        regionMaterials.Clear();

        // --- NEW --- Clear logged warnings on full clear
        loggedWarnings.Clear();

        if (enableVerboseLogging) Debug.Log("Voronoi Overlay Cleared.");
    }

    // This is the main generation method called via the interface
    public void ApplyQueuedMergesAndRegenerate(List<StarSystemData> currentStarSystems)
    {
        // 0. Preparation
        ClearOverlay(); // Start fresh

        if (currentStarSystems == null) {
             Debug.LogWarning("Voronoi Overlay: Input star systems list is null. Aborting generation.");
             return; // Handle null list input
        }

         // Ensure list contains only non-null entries before processing
         List<StarSystemData> validStarSystems = currentStarSystems.Where(s => s != null).ToList();

        if (validStarSystems.Count == 0)
        {
            Debug.LogWarning("Voronoi Overlay: Star systems list is empty or contains only null entries. Aborting generation.");
            return;
        }

        // Store for potential later use by RegenerateOverlay
        lastUsedStarSystems = new List<StarSystemData>(validStarSystems); // Store the validated list

        if (cellFillMaterialTemplate == null || cellBorderMaterial == null)
        {
            Debug.LogError("Voronoi Overlay: Materials not assigned. Aborting generation.");
            return;
        }

        if (enableVerboseLogging) Debug.Log($"Starting Voronoi generation for {validStarSystems.Count} initial stars.");

        // --- 1. Prepare REAL Star Data, handle queued merges BEFORE generating points ---
        HashSet<StarSystemData> starsForVoronoi = new HashSet<StarSystemData>(validStarSystems);
        Dictionary<StarSystemData, StarSystemData> mergeTargets = new Dictionary<StarSystemData, StarSystemData>(); // Merged star -> Its final target

        // Process queued merges to determine the final set of sites
        if(enableVerboseLogging && queuedMerges.Count > 0) Debug.Log($"Processing {queuedMerges.Count} queued merge requests...");
        foreach (var mergeRequest in queuedMerges)
        {
            StarSystemData targetStar = mergeRequest.target;
            StarSystemData sourceStar = mergeRequest.source;

            // Resolve multi-step merges (find the ultimate target)
            while (mergeTargets.ContainsKey(targetStar))
            {
                targetStar = mergeTargets[targetStar];
            }

            // Check if both are still valid candidates *before* attempting removal
            if (starsForVoronoi.Contains(sourceStar) && starsForVoronoi.Contains(targetStar) && sourceStar != targetStar)
            {
                bool removed = starsForVoronoi.Remove(sourceStar); // This star's zone gets merged away
                if(removed) {
                    mergeTargets[sourceStar] = targetStar; // Track where it went
                    if (enableVerboseLogging) Debug.Log($"Processed merge: {sourceStar.name} into {targetStar.name}. '{sourceStar.name}' removed from site candidates.");
                } else {
                     // This shouldn't typically happen if the initial checks pass, but log just in case.
                     Debug.LogWarning($"Merge Warning: Tried to remove '{sourceStar.name}' for merge into '{targetStar.name}', but it was already removed from the candidate set (possibly by a previous merge in this batch).");
                }
            }
            else
            {
                 // Log detailed reason for skipping
                 string reason = "";
                 if (sourceStar == null) reason += "Source star is null. ";
                 else if (!starsForVoronoi.Contains(sourceStar)) reason += $"Source star '{sourceStar.name}' not in candidate set (already merged?). ";
                 if (targetStar == null) reason += "Target star is null. ";
                 else if (!starsForVoronoi.Contains(targetStar)) reason += $"Target star '{targetStar.name}' not in candidate set (already merged?). ";
                 if (sourceStar == targetStar && sourceStar != null) reason += "Source and Target are the same star. ";
                 Debug.LogWarning($"Skipping invalid merge request: {sourceStar?.name ?? "NULL"} into {targetStar?.name ?? "NULL"}. Reason: {reason}");
            }
        }
        queuedMerges.Clear(); // Merges have been processed

        // Now use 'starsForVoronoi' (the remaining stars) to generate the actual diagram
        siteToStarDataMap.Clear();
        starDataToSiteMap.Clear();
        List<IPoint> realDelaunatorPoints = new List<IPoint>();
        float maxActualRadiusSqr = 0f;
        int realSiteCounter = 0;
        // Clear and rebuild global site mapping dictionaries for THIS generation run
        siteIndexToPos.Clear();
        sitePosToIndex.Clear();

        foreach (var star in starsForVoronoi) // Iterate over the filtered set
        {
            // star is guaranteed not null here due to initial filtering
            Vector2 sitePos = new Vector2(star.transform.position.x, star.transform.position.z);

            // Check for duplicate positions among the *final* set of stars
            if (!siteToStarDataMap.ContainsKey(sitePos))
            {
                siteToStarDataMap.Add(sitePos, star);
                starDataToSiteMap.Add(star, sitePos); // Add reverse mapping
                realDelaunatorPoints.Add(new Point(sitePos.x, sitePos.y));
                maxActualRadiusSqr = Mathf.Max(maxActualRadiusSqr, sitePos.sqrMagnitude);

                // Map this REAL site's index and position
                siteIndexToPos[realSiteCounter] = sitePos;
                sitePosToIndex[sitePos] = realSiteCounter;
                realSiteCounter++;
            }
            else
            {
                // This indicates two distinct StarSystemData objects *remaining after merges*
                // ended up at the exact same XZ position.
                 StarSystemData existingStar = siteToStarDataMap[sitePos];
                 LogWarningOnceStatic($"Duplicate Voronoi site position {sitePos} for stars '{star.systemName}' and '{existingStar.systemName}' after merge processing. Only one star's zone will be generated at this location. Check for identical star positions.", star);
            }
        }

        if (realDelaunatorPoints.Count == 0) {
            Debug.LogWarning("No valid unique star positions remaining after merge processing and position checks. No overlay generated.");
            ClearOverlay(); // Ensure cleanup even if nothing was generated
            return;
        }

        float maxActualRadius = Mathf.Sqrt(maxActualRadiusSqr);
        calculatedFakePointRadius = maxActualRadius + fakePointOffset;
        if (enableVerboseLogging) Debug.Log($"Max real star radius: {maxActualRadius:F2}, Fake point circle radius: {calculatedFakePointRadius:F2}. Final real sites: {realDelaunatorPoints.Count}");


        // --- 2. Generate FAKE Points ---
        List<IPoint> fakeDelaunatorPoints = GenerateCircularFakePoints(Vector2.zero, calculatedFakePointRadius, numFakePoints);
        if (enableVerboseLogging) Debug.Log($"Generated {fakeDelaunatorPoints.Count} fake points.");


        // --- 3. Combine Points & Prepare Mappings for ALL points ---
        List<IPoint> allDelaunatorPoints = new List<IPoint>(realDelaunatorPoints.Count + fakeDelaunatorPoints.Count);
        allDelaunatorPoints.AddRange(realDelaunatorPoints);
        allDelaunatorPoints.AddRange(fakeDelaunatorPoints);

        List<Vector2> allSites = new List<Vector2>(allDelaunatorPoints.Count);
        // Add fake points to the global mapping dictionaries
        int currentFakeIndexOffset = realDelaunatorPoints.Count; // Start indexing fakes after reals
        for (int i = 0; i < fakeDelaunatorPoints.Count; i++) {
             int globalIndex = currentFakeIndexOffset + i;
             Vector2 fakeSitePos = new Vector2((float)fakeDelaunatorPoints[i].X, (float)fakeDelaunatorPoints[i].Y);
             allSites.Add(fakeSitePos); // Add to list used by Delaunator computation

             // Map this FAKE site's index and position, checking for rare collisions with real sites
             if (!sitePosToIndex.ContainsKey(fakeSitePos)) {
                 sitePosToIndex.Add(fakeSitePos, globalIndex);
             } else {
                 // A fake point landed exactly on a real point (or another fake point).
                 // Keep the mapping for the REAL point (lower index) in sitePosToIndex.
                 // The fake point still exists in the 'allDelaunatorPoints' list for triangulation.
                 if(enableVerboseLogging) Debug.Log($"Fake point at {fakeSitePos} collided with an existing site mapping. Index {globalIndex} maps to pos, but pos maps back to lower index {sitePosToIndex[fakeSitePos]}.");
             }
             siteIndexToPos[globalIndex] = fakeSitePos; // Always map index -> pos
        }
        // Add real site positions to the allSites list *after* fake points are added to siteIndexToPos
        // to ensure allSites has the correct order matching allDelaunatorPoints.
        allSites.InsertRange(0, siteToStarDataMap.Keys);


        if (allDelaunatorPoints.Count < 3) {
            Debug.LogWarning($"Not enough total points ({allDelaunatorPoints.Count}) for triangulation. Required >= 3.");
            ClearOverlay(); return;
        }
        Debug.Log($"Generating diagram using {realDelaunatorPoints.Count} final real sites and {fakeDelaunatorPoints.Count} fake sites ({allDelaunatorPoints.Count} total points for triangulation).");


        // Create container GameObject
        overlayContainer = new GameObject("VoronoiOverlay");
        overlayContainer.transform.SetParent(this.transform, false); // Parent to this object
        overlayContainer.transform.localPosition = Vector3.zero; // Reset local position


        // --- 4. Compute Voronoi Diagram & Adjacency ---
        calculatedCircumcenters = null;
        currentDelaunator = null;
        Dictionary<Vector2, List<Vector2>> voronoiCells = null;
        try
        {
            // Pass the set of site positions corresponding to the final real stars
            voronoiCells = ComputeVoronoiAndAdjacency(
                allDelaunatorPoints.ToArray(),
                allSites, // List of all Vector2 positions
                circumcenterCheckBounds,
                siteToStarDataMap.Keys.ToHashSet() // Use the sites remaining after merge filtering
            );
        } catch (System.Exception ex) {
             Debug.LogError($"Voronoi/Adjacency computation error: {ex.Message}\n{ex.StackTrace}");
             ClearOverlay(); return;
        }

        if (voronoiCells == null || currentDelaunator == null || calculatedCircumcenters == null) {
             Debug.LogError("Voronoi cell, Delaunator, or Circumcenter computation failed critically.");
             ClearOverlay(); return;
        }


        // --- 5. Perform Specific Internal Merge Test (Optional) ---
        // This runs *after* the main Voronoi calculation and external merges.
        // It operates directly on the `voronoiCells` dictionary.
        if (enableSpecificMergeTest && siteToStarDataMap.Count >= mergeTestCentralStars + mergeTestNeighborsToMerge) // Need enough stars *remaining*
        {
            // Pass the current cells and the list of *final* real sites
            Debug.LogWarning("Running internal specific merge test. This modifies the calculated Voronoi cells directly.");
            PerformSpecificMergeTest(voronoiCells, siteToStarDataMap.Keys.ToList());
        }
        else if (enableSpecificMergeTest)
        {
             Debug.LogWarning($"Skipping specific internal merge test: Not enough final real stars ({siteToStarDataMap.Count}) available for the requested configuration ({mergeTestCentralStars} central + {mergeTestNeighborsToMerge} neighbors each).");
        }


        // --- 6. Generate Meshes for the FINAL regions ---
        Debug.Log($"Generating meshes for {voronoiCells.Count} potential regions (will filter for final real sites)...");
        int meshCount = 0;
        starToFillObjectMap.Clear(); // Clear old mappings before generating new ones
        starToBorderObjectMap.Clear();

        // Iterate through the calculated cells
        foreach (var kvpCell in voronoiCells)
        {
            Vector2 site = kvpCell.Key;
            List<Vector2> originalPolygonVertices = kvpCell.Value; // Keep original for reference if needed

            // IMPORTANT: Check if this site corresponds to a final star
            if (siteToStarDataMap.TryGetValue(site, out StarSystemData correspondingStar))
            {
                // Site is a final real site, process its vertices for visuals
                if (originalPolygonVertices != null && originalPolygonVertices.Count >= 3)
                {
                    if (enableVerboseLogging) Debug.Log($"Processing Star {correspondingStar.name} at {site}. Original vertex count: {originalPolygonVertices.Count}");

                    // --- Filtering Pipeline ---
                    List<Vector2> verticesNoBacktracks = PolygonUtils.FilterBacktracks(originalPolygonVertices, enableVerboseLogging);
                    // ... (optional logging) ...

                    List<Vector2> verticesDistFiltered = PolygonUtils.FilterClosePoints(verticesNoBacktracks, vertexDistanceThreshold, enableVerboseLogging);
                    // ... (optional logging) ...

                    // Finalize loop AND check if it's considered closed
                    PolygonUtils.FinalizeLoopResult finalizedResult = PolygonUtils.FinalizeLoop(verticesDistFiltered, enableVerboseLogging);
                    List<Vector2> finalFilteredVertices = finalizedResult.Vertices;
                    bool isClosedLoop = finalizedResult.IsClosed;
                    // ... (optional logging) ...

                    // --- Smoothing Step (Conditional) ---
                    List<Vector2> verticesForMesh = finalFilteredVertices; // Default to filtered vertices
                    if (enableSmoothing && finalFilteredVertices.Count >= 2) // Chaikin needs at least 2 points
                    {
                        if (enableVerboseLogging) Debug.Log($" -> Applying Chaikin smoothing ({chaikinIterations} iterations, Closed={isClosedLoop})...");
                        verticesForMesh = PolygonUtils.ChaikinSmooth(finalFilteredVertices, chaikinIterations, 0.25f, isClosedLoop); // Using 0.25 ratio
                        if (enableVerboseLogging) Debug.Log($" -> After smoothing: {verticesForMesh.Count} vertices.");
                    }
                    else if (enableSmoothing && enableVerboseLogging)
                    {
                         Debug.Log($" -> Skipping smoothing: Not enough vertices after filtering ({finalFilteredVertices.Count}).");
                    }


                    // --- Generate Visuals using FINAL (potentially smoothed) Vertices ---
                    // Check if enough vertices remain *after* filtering AND potential smoothing
                    if (verticesForMesh.Count >= 3) // Mesh generation still needs at least 3 points
                    {
                        if (enableVerboseLogging) Debug.Log($" -> Final vertex count for mesh generation: {verticesForMesh.Count}. Creating meshes.");
                        // Pass the potentially smoothed vertices to your mesh creation functions
                        CreateCellFillMesh(site, correspondingStar, verticesForMesh, overlayContainer.transform);
                        // NOTE: You might need to adjust CreateCellBorderMesh if it relies on sharp corners
                        // or pass isClosedLoop to it if it needs to handle closing the border line itself.
                        CreateCellBorderMesh(correspondingStar, verticesForMesh, overlayContainer.transform); // Modified signature assumed
                        meshCount++;
                    }
                    else
                    {
                        // Log why mesh generation is skipped
                        if (enableVerboseLogging) Debug.LogWarning($"Skipping mesh for final region at {site} (Star: {correspondingStar.name}): Not enough vertices for mesh ({verticesForMesh.Count}) AFTER filtering/smoothing. Original count: {originalPolygonVertices.Count}, Filtered count: {finalFilteredVertices.Count}.");
                    }
                }
                else
                {
                     // Original vertex list was null or too small
                     if (enableVerboseLogging) Debug.LogWarning($"Skipping mesh for final region at {site} (Star: {correspondingStar.name}): Initial vertex list null or too small ({originalPolygonVertices?.Count ?? 0}).");
                }
            }
            else
            {
                // This site is NOT one of the final real sites. (Your existing logic)
                // ... (your existing logging for skipped/fake sites) ...
            }
        }
            Debug.Log($"Generated {meshCount} final cell meshes ({generatedOverlayObjects.Count} total visual GameObjects created).");
    }

    // Regenerates using the last known star list, without applying new merges
    public void RegenerateOverlay()
    {
        if (lastUsedStarSystems == null)
        {
            Debug.LogWarning("Cannot regenerate overlay: No previous star system data available. Call ApplyQueuedMergesAndRegenerate first.");
            return;
        }
        Debug.Log("Regenerating Voronoi overlay using last known star data (no new merges applied).");
        // Call ApplyQueuedMergesAndRegenerate, ensuring the queue is empty first
        ClearMergeRequests(); // Ensure no accidental merges from queue
        ApplyQueuedMergesAndRegenerate(lastUsedStarSystems);
    }

    // --- Interface Implementations ---

    public StarSystemData GetStarSystemFromSite(Vector2 sitePosition)
    {
        // Use the map populated during the last successful generation
        siteToStarDataMap.TryGetValue(sitePosition, out StarSystemData star);
        return star;
    }

    public bool GetSiteFromStarSystem(StarSystemData star, out Vector2 sitePosition)
    {
        // Use the map populated during the last successful generation
        return starDataToSiteMap.TryGetValue(star, out sitePosition);
    }

    public bool EnableZone(StarSystemData star)
    {
         if (star == null) return false;
        bool fillFound = starToFillObjectMap.TryGetValue(star, out GameObject fillObj);
        bool borderFound = starToBorderObjectMap.TryGetValue(star, out GameObject borderObj);

        // Important: Check if the GameObjects still exist (might have been destroyed)
        if (fillFound && fillObj != null) fillObj.SetActive(true);
        if (borderFound && borderObj != null) borderObj.SetActive(true);

        // Return true only if at least one valid, existing part was found and activated
        return (fillFound && fillObj != null) || (borderFound && borderObj != null);
    }

    public bool DisableZone(StarSystemData star)
    {
        if (star == null) return false;
        bool fillFound = starToFillObjectMap.TryGetValue(star, out GameObject fillObj);
        bool borderFound = starToBorderObjectMap.TryGetValue(star, out GameObject borderObj);

        // Important: Check if the GameObjects still exist
        if (fillFound && fillObj != null) fillObj.SetActive(false);
        if (borderFound && borderObj != null) borderObj.SetActive(false);

        // Return true only if at least one valid, existing part was found and deactivated
        return (fillFound && fillObj != null) || (borderFound && borderObj != null);
    }

     // Helper to get the material associated with a star's final region
     private Material GetMaterialForStar(StarSystemData star) {
         if (star != null && starDataToSiteMap.TryGetValue(star, out Vector2 site)) {
             if (regionMaterials.TryGetValue(site, out Material mat)) {
                 return mat; // Return the found material
             }
         }
         // Log if lookup failed? Only if verbose?
         // if (enableVerboseLogging && star != null) Debug.Log($"GetMaterialForStar: Could not find site or material for star '{star.name}'.");
         return null; // Return null if star, site, or material not found
     }

    public bool SetZoneColor(StarSystemData star, Color color)
    {
         if (star == null) return false;
        Material mat = GetMaterialForStar(star);
         if (mat != null) {
             if (!string.IsNullOrEmpty(fillColorPropertyName) && mat.HasProperty(fillColorPropertyName)) {
                 mat.SetColor(fillColorPropertyName, color);
                 return true;
             } else {
                 LogWarningOnceStatic($"Cannot set zone color for {star.name}: Fill Color Property Name ('{fillColorPropertyName}') is not set or invalid for the material '{mat.name}'.", this);
             }
         }
         return false;
    }

    public bool GetZoneColor(StarSystemData star, out Color color)
    {
        color = Color.clear; // Default value
        if (star == null) return false;
        Material mat = GetMaterialForStar(star);
         if (mat != null) {
             if (!string.IsNullOrEmpty(fillColorPropertyName) && mat.HasProperty(fillColorPropertyName)) {
                 color = mat.GetColor(fillColorPropertyName);
                 return true;
             }
         }
        return false;
    }

    public bool SetZoneMaterialFloat(StarSystemData star, string propertyName, float value)
    {
        if (star == null) return false;
        Material mat = GetMaterialForStar(star);
        if (mat != null) {
            if (mat.HasProperty(propertyName)) {
                mat.SetFloat(propertyName, value);
                return true;
            } else {
                 if(enableVerboseLogging) Debug.LogWarning($"SetZoneMaterialFloat: Material for star '{star.name}' does not have property '{propertyName}'.", this);
            }
        }
        return false;
    }

    public bool SetZoneMaterialColor(StarSystemData star, string propertyName, Color color)
    {
         if (star == null) return false;
         Material mat = GetMaterialForStar(star);
         if (mat != null) {
             if (mat.HasProperty(propertyName)) {
                 mat.SetColor(propertyName, color);
                 return true;
             } else {
                  if(enableVerboseLogging) Debug.LogWarning($"SetZoneMaterialColor: Material for star '{star.name}' does not have property '{propertyName}'.", this);
             }
         }
         return false;
    }

    public bool QueueMergeRequest(StarSystemData starToMergeInto, StarSystemData starToMergeFrom)
    {
        if (starToMergeInto == null || starToMergeFrom == null)
        {
            Debug.LogError($"Invalid merge request: Target or Source star is null.");
            return false;
        }
         if (starToMergeInto == starToMergeFrom) {
             Debug.LogError($"Invalid merge request: Cannot merge star '{starToMergeInto.name}' into itself.");
             return false;
         }

        // Optional: Check if these stars are actually neighbours based on the *last* generation's adjacency?
        // This requires adjacency info to persist correctly between generations.
        /*
        if (starDataToSiteMap.TryGetValue(starToMergeInto, out Vector2 siteInto) &&
            starDataToSiteMap.TryGetValue(starToMergeFrom, out Vector2 siteFrom))
        {
            if (!realSiteAdjacency.TryGetValue(siteInto, out List<Vector2> neighbors) || !neighbors.Contains(siteFrom))
            {
                Debug.LogWarning($"QueueMergeRequest: Target '{starToMergeInto.name}' and Source '{starToMergeFrom.name}' were not adjacent in the last generated overlay. Queuing anyway.");
                // Decide whether to prevent non-adjacent merges or just warn
                // return false; // Uncomment to prevent
            }
        } else {
             Debug.LogWarning($"QueueMergeRequest: Could not verify adjacency for '{starToMergeInto.name}' and '{starToMergeFrom.name}' as one or both were not found in the last site map. Queuing anyway.");
        }
        */

        queuedMerges.Add((starToMergeInto, starToMergeFrom));
        if(enableVerboseLogging) Debug.Log($"Queued merge request: {starToMergeFrom.name} into {starToMergeInto.name}. (Total queued: {queuedMerges.Count})");
        return true;
    }

    public void ClearMergeRequests()
    {
        int count = queuedMerges.Count;
        queuedMerges.Clear();
        if(enableVerboseLogging && count > 0) Debug.Log($"Cleared {count} queued merge requests.");
    }
    #endregion

    // --- Internal Generation Logic (Called by ApplyQueuedMergesAndRegenerate) ---

    #region Fake Point Generation & Check
    private List<IPoint> GenerateCircularFakePoints(Vector2 center, float radius, int numPoints) {
        List<IPoint> fakePoints = new List<IPoint>();
        if (radius <= 0 || numPoints <= 0) {
            Debug.LogWarning("GenerateCircularFakePoints: Radius or numPoints is zero or negative. Returning empty list.");
            return fakePoints;
        }
        float angleStep = 2 * Mathf.PI / numPoints;
        for (int i = 0; i < numPoints; i++) {
            float angle = i * angleStep;
            // Using double precision as DelaunatorSharp expects IPoint with doubles
            double x = center.x + radius * System.Math.Cos(angle);
            double y = center.y + radius * System.Math.Sin(angle);
            fakePoints.Add(new Point(x, y));
        }
        return fakePoints;
    }

    // Checks if a site corresponds to a fake point based on its global index
    private bool IsFakeSite(Vector2 sitePos, int realPointCount, List<Vector2> allSitesList) {
         // Prioritize checking the index mapping first, as it's more direct
        if (sitePosToIndex.TryGetValue(sitePos, out int index)) {
            // Indices >= realPointCount belong to fake points
            return index >= realPointCount;
        }

        // Fallback check by comparing positions (less reliable due to float precision)
        // This might be needed if sitePosToIndex lookup fails for some reason
        // Only check indices in the range where fake points *should* be
        float epsilonSqr = Epsilon * Epsilon;
        for (int i = realPointCount; i < allSitesList.Count; i++) {
             // Use SqrMagnitude for efficiency
            if (Vector2.SqrMagnitude(allSitesList[i] - sitePos) < epsilonSqr) {
                 if(enableVerboseLogging) Debug.LogWarning($"IsFakeSite: Site {sitePos} identified as fake via position comparison (index {i}). Index lookup failed.");
                return true; // Found a matching position in the fake point range
            }
        }
         if(enableVerboseLogging) Debug.LogWarning($"IsFakeSite: Site {sitePos} could not be found in sitePosToIndex map and did not match any fake point positions. Assuming not fake.");
        return false; // Not found in map or by position comparison
    }
    #endregion

    #region Core Voronoi & Adjacency Calculation
    // Helper to get the next edge index in a triangle (0->1, 1->2, 2->0)
    private static int NextHalfedgeIndex(int index) => (index % 3 == 2) ? index - 2 : index + 1;

    // Computes the Voronoi diagram and adjacency between the final real sites
    private Dictionary<Vector2, List<Vector2>> ComputeVoronoiAndAdjacency(
        IPoint[] points, // All points (real + fake)
        List<Vector2> sites, // All Vector2 positions (matching points order)
        Rect circumcenterCheckBounds,
        HashSet<Vector2> finalRealSitePositions) // Positions of stars remaining after external merges
    {
        var voronoiCells = new Dictionary<Vector2, List<Vector2>>();
        realSiteAdjacency.Clear(); // Clear adjacency from previous runs
        currentDelaunator = null;
        calculatedCircumcenters = null;

        // --- Delaunay Triangulation ---
        try {
             currentDelaunator = new Delaunator(points);
        }
        catch (System.ArgumentException argEx) { // Catch specific exception from Delaunator
             Debug.LogError($"Delaunator construction failed: {argEx.Message}. This often happens with too few points or coincident/collinear points.", this);
             return null; // Indicate failure
        }
        catch (System.Exception ex) { // Catch other unexpected errors
             Debug.LogError($"Delaunator construction failed with unexpected error: {ex.Message}\n{ex.StackTrace}", this);
             return null; // Indicate failure
        }

        // Check if triangulation results are valid
        if (currentDelaunator.Triangles == null || currentDelaunator.Halfedges == null || currentDelaunator.Triangles.Length == 0) {
             Debug.LogWarning("Delaunator generated no triangles or halfedges. Cannot compute Voronoi cells.", this);
             return voronoiCells; // Return empty cells, but not null (as Delaunator itself didn't throw)
        }
         if (currentDelaunator.Triangles.Length % 3 != 0) {
              Debug.LogError($"Delaunator generated an invalid number of triangle indices ({currentDelaunator.Triangles.Length}), not divisible by 3.", this);
              return null; // Indicate critical failure
         }


        // --- Circumcenter Calculation ---
        int numTriangles = currentDelaunator.Triangles.Length / 3;
        Vector2?[] circumcenters = new Vector2?[numTriangles]; // Array to store nullable circumcenters
        for (int t = 0; t < numTriangles; t++) {
            // Get vertex indices for this triangle
            int p1Idx = currentDelaunator.Triangles[3*t + 0];
            int p2Idx = currentDelaunator.Triangles[3*t + 1];
            int p3Idx = currentDelaunator.Triangles[3*t + 2];

            // Validate indices (shouldn't happen with a valid Delaunator result, but good practice)
            if (p1Idx < 0 || p1Idx >= points.Length ||
                p2Idx < 0 || p2Idx >= points.Length ||
                p3Idx < 0 || p3Idx >= points.Length) {
                 Debug.LogError($"Triangle {t} has invalid point index. p1:{p1Idx}, p2:{p2Idx}, p3:{p3Idx}. Max index: {points.Length-1}. Aborting circumcenter calculation.", this);
                 return null; // Critical error
            }

            // Calculate circumcenter
            Vector2? center = CalculateCircumcenter(points[p1Idx], points[p2Idx], points[p3Idx]);

            // Check if calculation failed or if center is outside specified bounds
            if (center.HasValue && !circumcenterCheckBounds.Contains(center.Value)) {
                 if (enableVerboseLogging) Debug.Log($"Circumcenter for triangle {t} ({center.Value}) is outside bounds {circumcenterCheckBounds}. Treating as null.");
                 circumcenters[t] = null; // Treat as invalid if outside bounds
            } else if (!center.HasValue && enableVerboseLogging) {
                 Debug.Log($"Circumcenter calculation failed for triangle {t} (vertices likely collinear).");
                 circumcenters[t] = null; // Store null if calculation failed
            } else {
                 circumcenters[t] = center; // Store valid circumcenter
            }
        }
        this.calculatedCircumcenters = circumcenters; // Store the computed circumcenters


        // --- Voronoi Cell Construction & Adjacency Calculation ---
        realSiteAdjacency.Clear(); // Explicitly clear adjacency again before populating

        // Iterate through each input point (site)
        for (int siteIndex = 0; siteIndex < points.Length; siteIndex++) {
            Vector2 sitePos;
            // Get the position of the current site using the index->position map
            if (!siteIndexToPos.TryGetValue(siteIndex, out sitePos))
            {
                 // This should not happen if siteIndexToPos was populated correctly for all points
                 Debug.LogError($"Voronoi Generation Error: Could not find position mapping for site index {siteIndex}. Aborting cell generation.", this);
                 return null; // Critical mapping error
            }

            // Determine if this site corresponds to one of the FINAL real stars (after external merges)
            bool isFinalRealSite = finalRealSitePositions.Contains(sitePos);

            // Initialize adjacency list only for sites that are final real sites
            if (isFinalRealSite && !realSiteAdjacency.ContainsKey(sitePos)) {
                 realSiteAdjacency[sitePos] = new List<Vector2>();
            }

            // Find an incoming half-edge for the current siteIndex
            // This is an edge where the *end* vertex (calculated by NextHalfedgeIndex) is our site.
            int startEdge = -1;
            for (int e = 0; e < currentDelaunator.Triangles.Length; e++) {
                 if (currentDelaunator.Triangles[NextHalfedgeIndex(e)] == siteIndex) {
                      startEdge = e;
                      break; // Found the first incoming edge
                 }
            }

            // If no incoming edge is found, the site might be isolated or on the convex hull edge case.
            if (startEdge == -1) {
                 if (enableVerboseLogging) Debug.LogWarning($"No starting incoming half-edge found for site {siteIndex} at {sitePos}. May be isolated or on hull boundary. Skipping cell generation for this site.");
                 continue; // Skip this site
            }

            // Collect the vertices (circumcenters) for this site's Voronoi cell
            List<Vector2> cellVertices = new List<Vector2>();
            int currentEdge = startEdge; // Start traversing from the found edge
            int iter = 0; // Safety counter to prevent infinite loops

            do { // Loop through edges surrounding the site
                 iter++;
                 if (iter > maxLoopIterationsPerSite) {
                     Debug.LogError($"SAFETY BREAK: Exceeded max loop iterations ({maxLoopIterationsPerSite}) while constructing cell for Site {siteIndex} at {sitePos}. Cell will be incomplete.", this);
                     cellVertices.Clear(); // Discard potentially corrupt cell data
                     break; // Exit the do-while loop
                 }

                 // Calculate the triangle ID for the current edge
                 int triangleId = currentEdge / 3;

                 // Validate triangle ID before accessing circumcenters array
                 if (triangleId < 0 || triangleId >= this.calculatedCircumcenters.Length) {
                     Debug.LogError($"Invalid Triangle ID {triangleId} derived from edge {currentEdge} for site {siteIndex}. Max T ID: {this.calculatedCircumcenters.Length-1}. Cell generation aborted for this site.", this);
                     cellVertices.Clear(); // Discard potentially corrupt cell data
                     break; // Exit the do-while loop
                 }

                 // Add the circumcenter of the current triangle to the cell vertices if it's valid
                 if (this.calculatedCircumcenters[triangleId].HasValue) {
                      Vector2 cCenter = this.calculatedCircumcenters[triangleId].Value;
                      // Add vertex only if it's not identical to the last one (prevents duplicate vertices)
                      if (cellVertices.Count == 0 || Vector2.SqrMagnitude(cellVertices[cellVertices.Count - 1] - cCenter) > Epsilon * Epsilon) {
                           cellVertices.Add(cCenter);
                      }
                 } else {
                      // Circumcenter is null (collinear points or outside bounds)
                      if (enableVerboseLogging) Debug.LogWarning($"Site {siteIndex} ({sitePos}): Circumcenter for Triangle {triangleId} is null. Cell may be open or incomplete.");
                      // Consider how to handle open cells if necessary - currently just leads to fewer vertices
                 }

                 // --- Adjacency Check ---
                 // Find the edge going *out* from our site in this triangle
                 int outgoingEdge = NextHalfedgeIndex(currentEdge);
                 // Get the opposite half-edge from the Delaunator data
                 if (outgoingEdge < 0 || outgoingEdge >= currentDelaunator.Halfedges.Length) {
                      Debug.LogError($"Invalid OutgoingEdge index {outgoingEdge} calculated for site {siteIndex}. Adjacency check skipped for this edge.", this);
                      // Decide how to proceed - break? continue? For now, just skip adjacency for this edge.
                 }
                 else
                 {
                     int oppositeEdge = currentDelaunator.Halfedges[outgoingEdge];

                     // Check adjacency ONLY if the current site is a final real site AND there's an adjacent triangle (oppositeEdge != -1)
                     if (isFinalRealSite && oppositeEdge != -1) {
                          // Get the index of the site in the adjacent triangle
                          int neighborSiteIndex = currentDelaunator.Triangles[oppositeEdge];

                          // Validate neighbor index and get its position
                          if (neighborSiteIndex >= 0 && siteIndexToPos.TryGetValue(neighborSiteIndex, out Vector2 neighborPos)) {
                               // Check if the NEIGHBOR is ALSO a final real site
                               if (finalRealSitePositions.Contains(neighborPos)) {
                                    // Add neighbor relationship if not already present
                                    // Add to current site's list
                                    if (!realSiteAdjacency[sitePos].Contains(neighborPos)) {
                                         realSiteAdjacency[sitePos].Add(neighborPos);
                                    }
                                    // Ensure reciprocal entry exists for the neighbor and add if needed
                                    if (!realSiteAdjacency.ContainsKey(neighborPos)) {
                                         // This case (neighbor not having an entry) should ideally not happen
                                         // if the loop iterates correctly, but handle defensively.
                                         realSiteAdjacency[neighborPos] = new List<Vector2>();
                                         if (enableVerboseLogging) Debug.LogWarning($"Adjacency: Created missing list for neighbor {neighborSiteIndex} ({neighborPos}) while processing site {siteIndex} ({sitePos}).");
                                    }
                                    if (!realSiteAdjacency[neighborPos].Contains(sitePos)) {
                                         realSiteAdjacency[neighborPos].Add(sitePos);
                                    }
                               }
                               // else: Neighbor is not a *final* real site (it's fake or was merged away), so no adjacency recorded.
                          } else {
                               Debug.LogWarning($"Invalid neighbor site index {neighborSiteIndex} (from opposite edge {oppositeEdge}) or position lookup failed when checking adjacency for site {siteIndex} ({sitePos}). Max index: {points.Length - 1}");
                          }
                     }
                     // --- End Adjacency Check ---

                     // Move to the next edge around the site
                     currentEdge = oppositeEdge; // Follow the link to the next triangle's edge
                 }


                 // Check loop termination conditions
                 if (currentEdge == -1) { // Reached the hull of the triangulation
                      if (isFinalRealSite && enableVerboseLogging) Debug.LogWarning($"Site {siteIndex} ({sitePos}): Hit hull edge (-1) during vertex traversal. Cell may be open or incomplete due to null circumcenters.");
                      break; // Exit the do-while loop for hull edges
                 }
                 if (currentEdge == startEdge) { // Completed the loop around the site
                      break; // Exit the do-while loop
                 }

            } while (true); // Continue until break condition is met


            // --- Store Completed Cell ---
            // Store the calculated vertices for this site's cell, regardless of whether it's
            // a final real site, a fake site, or an intermediate merged site, as the geometry
            // might be needed by internal merging logic if enabled.
            // Filtering for mesh generation happens later.
            if (cellVertices.Count >= 3) {
                 // Clean up the polygon (remove duplicate vertices, etc.)
                 List<Vector2> cleaned = CleanPolygon(cellVertices);
                 if (cleaned.Count >= 3) {
                      // Ensure vertices are ordered correctly (e.g., clockwise) for triangulation
                      voronoiCells[sitePos] = OrderVerticesClockwise(cleaned);
                 } else {
                      if (enableVerboseLogging) Debug.LogWarning($"Site {siteIndex} ({sitePos}): Cell discarded after cleaning (vertices < 3). Original count: {cellVertices.Count}");
                 }
            } else if (cellVertices.Count > 0) {
                 // Cell generation finished but didn't yield enough vertices for a polygon
                 if (enableVerboseLogging) Debug.LogWarning($"Site {siteIndex} ({sitePos}): Cell discarded (vertices < 3). Final count: {cellVertices.Count}. Might be due to null circumcenters or being on the hull.");
            }
            // If cellVertices.Count is 0, it means something went wrong (e.g., safety break), logged earlier.
        } // End of loop through all sites

        // Return the dictionary mapping site positions to their calculated Voronoi cell vertices
        return voronoiCells;
    }

    // Calculates the circumcenter of a triangle defined by three points
    private Vector2? CalculateCircumcenter(IPoint p1, IPoint p2, IPoint p3) {
        // Using double precision for calculations matching IPoint
        double ax = p1.X, ay = p1.Y;
        double bx = p2.X, by = p2.Y;
        double cx = p3.X, cy = p3.Y;

        // Calculate the denominator D
        double D = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));

        // Check for collinear points (D is close to zero)
        if (System.Math.Abs(D) < Epsilon * Epsilon) { // Use a small epsilon comparison
            // Points are collinear, circumcenter is undefined (or at infinity)
            return null;
        }

        // Calculate circumcenter coordinates using the formula
        double cX_d = ((ax*ax + ay*ay) * (by - cy) + (bx*bx + by*by) * (cy - ay) + (cx*cx + cy*cy) * (ay - by)) / D;
        double cY_d = ((ax*ax + ay*ay) * (cx - bx) + (bx*bx + by*by) * (ax - cx) + (cx*cx + cy*cy) * (bx - ax)) / D;

        // Check for potential NaN or Infinity results (should be rare if D check passes)
        if (double.IsNaN(cX_d) || double.IsNaN(cY_d) || double.IsInfinity(cX_d) || double.IsInfinity(cY_d)) {
            Debug.LogWarning($"CalculateCircumcenter resulted in NaN or Infinity for points A({ax},{ay}), B({bx},{by}), C({cx},{cy}). Denominator D={D}");
            return null;
        }

        // Return the calculated circumcenter as a nullable Vector2
        return new Vector2((float)cX_d, (float)cY_d);
    }

    // Removes duplicate consecutive vertices and the closing duplicate vertex
    private List<Vector2> CleanPolygon(List<Vector2> vertices) {
        if (vertices == null || vertices.Count < 2) return vertices ?? new List<Vector2>(); // Handle null or small lists

        List<Vector2> cleaned = new List<Vector2>(vertices.Count);
        cleaned.Add(vertices[0]); // Start with the first vertex

        // Add subsequent vertices only if they are sufficiently far from the *last added* vertex
        for (int i = 1; i < vertices.Count; i++) {
            if (Vector2.SqrMagnitude(vertices[i] - cleaned[cleaned.Count - 1]) > Epsilon * Epsilon) {
                cleaned.Add(vertices[i]);
            }
        }

        // Check if the last vertex is very close to the first vertex (closing the loop)
        if (cleaned.Count > 1 && Vector2.SqrMagnitude(cleaned[cleaned.Count - 1] - cleaned[0]) < Epsilon * Epsilon) {
            cleaned.RemoveAt(cleaned.Count - 1); // Remove the closing duplicate
        }

        return cleaned;
    }

    // Ensures polygon vertices are ordered clockwise using the shoelace formula (signed area)
    private List<Vector2> OrderVerticesClockwise(List<Vector2> vertices) {
        if (vertices == null || vertices.Count < 3) return vertices; // Need at least 3 vertices

        float area = CalculateSignedArea(vertices);
        // If area is positive, the order is counter-clockwise (in typical screen coordinates)
        // Reverse the list to make it clockwise.
        if (area > 0) {
            vertices.Reverse();
        }
        // If area is negative, it's already clockwise. If zero, it's degenerate.
        return vertices;
    }
    #endregion

    #region Cell Merging (Internal - Optional, runs after main calc)
    // This section performs the optional internal merge test. It directly modifies
    // the 'voronoiCells' dictionary and 'realSiteAdjacency'.
    // Use with caution if external merging via QueueMergeRequest is the primary method.

    // Initiates the internal merge test based on inspector settings
    private void PerformSpecificMergeTest(Dictionary<Vector2, List<Vector2>> cells, List<Vector2> finalRealSitesList)
    {
        if (finalRealSitesList == null || finalRealSitesList.Count < 2 || realSiteAdjacency.Count == 0) {
            Debug.LogWarning("Internal Merge Test: Not enough final real sites or adjacency info available.");
            return;
        }
        if (currentDelaunator == null || calculatedCircumcenters == null) {
             Debug.LogError("Internal Merge Test cannot proceed: Delaunator or Circumcenters not available from the main calculation.");
             return;
        }

        // Track sites that get merged away during this internal test
        HashSet<Vector2> mergedSitesInternally = new HashSet<Vector2>();
        int mergeGroupsCreated = 0;
        List<Vector2> availableSites = new List<Vector2>(finalRealSitesList); // Work on a copy


        Debug.Log($"--- Starting Internal Specific Merge Test: {mergeTestCentralStars} groups, up to {mergeTestNeighborsToMerge} neighbors each ---");

        // Loop to create the desired number of merge groups
        while (mergeGroupsCreated < mergeTestCentralStars && availableSites.Count > 0)
        {
            // Pick a random available site as the center of the merge group
            int randomIndex = Random.Range(0, availableSites.Count);
            Vector2 centralSite = availableSites[randomIndex];
            availableSites.RemoveAt(randomIndex); // Remove from available pool

            // Skip if this site was already merged away in a previous iteration of *this* test
            if (mergedSitesInternally.Contains(centralSite)) continue;

            // Mark the central site as used (it won't be merged *into*)
            mergedSitesInternally.Add(centralSite); // Mark central site itself as 'used' in merging context
            int centralSiteIndex = sitePosToIndex.ContainsKey(centralSite) ? sitePosToIndex[centralSite] : -1; // For logging
            Debug.Log($"Internal Merge Group {mergeGroupsCreated + 1}: Central Site {centralSiteIndex} ({centralSite})");


            // Find valid neighbors to merge into the central site
            if (realSiteAdjacency.TryGetValue(centralSite, out List<Vector2> neighbors))
            {
                // Select neighbors that:
                // 1. Are not the central site itself.
                // 2. Haven't already been merged away *in this internal test*.
                // 3. Still exist in the 'cells' dictionary (haven't been removed by a previous merge in this test).
                // 4. Limit the number based on mergeTestNeighborsToMerge.
                List<Vector2> neighborsToMerge = neighbors
                    .Where(n => n != centralSite &&
                                !mergedSitesInternally.Contains(n) &&
                                cells.ContainsKey(n)) // Ensure neighbor cell exists
                    .Take(mergeTestNeighborsToMerge)
                    .ToList();

                int mergedNeighborCount = 0;
                foreach (Vector2 neighborSite in neighborsToMerge)
                {
                    int neighborSiteIndex = sitePosToIndex.ContainsKey(neighborSite) ? sitePosToIndex[neighborSite] : -1; // For logging

                    // Double-check central site still exists before attempting merge
                    if (!cells.ContainsKey(centralSite)) {
                         Debug.LogWarning($"    Internal Merge: Central site {centralSiteIndex} ({centralSite}) disappeared unexpectedly before merging neighbor {neighborSiteIndex}. Skipping merge.");
                         // Mark neighbor as used anyway to prevent trying it again? Or leave available?
                         // mergedSitesInternally.Add(neighborSite); // Optional: Mark as used even on failure
                         continue;
                    }
                    // Neighbor existence already checked by LINQ .Where(cells.ContainsKey(n))

                    if (enableVerboseLogging) Debug.Log($"  Internal Merge Attempt: Neighbor {neighborSiteIndex} ({neighborSite}) into Central {centralSiteIndex} ({centralSite})");

                    // Attempt the merge operation
                    bool success = MergeCells(cells, centralSite, neighborSite);

                    if (success)
                    {
                        // If merge succeeded:
                        // 1. Mark the neighbor site as merged away in this test.
                        // 2. Increment the count of successful merges for this group.
                        mergedSitesInternally.Add(neighborSite);
                        mergedNeighborCount++;
                        if (enableVerboseLogging) Debug.Log($"    Internal merge successful.");
                    }
                    else
                    {
                        // If merge failed, don't mark the neighbor as merged. It might be mergeable elsewhere.
                        Debug.LogWarning($"    Internal merge failed between {centralSiteIndex} ({centralSite}) and {neighborSiteIndex} ({neighborSite}). Neighbor remains available.");
                    }
                }
                 Debug.Log($"  Merged {mergedNeighborCount} neighbors into Central Site {centralSiteIndex} ({centralSite}).");

            } else {
                 // The central site might have no neighbors left after external merges, or adjacency calculation failed.
                 Debug.Log($"  Central Site {centralSiteIndex} ({centralSite}) has no recorded real neighbors in `realSiteAdjacency` or neighbors were already merged.");
            }

            mergeGroupsCreated++; // Increment the count of initiated merge groups
        } // End while loop for creating merge groups


        if (mergeGroupsCreated < mergeTestCentralStars)
        {
            Debug.LogWarning($"Internal Merge test finished early: Created {mergeGroupsCreated} groups (requested {mergeTestCentralStars}). Not enough available stars or neighbors?");
        }
         Debug.Log($"--- Internal Specific Merge Test Complete ---");
    }


    // Performs the geometric merging of two Voronoi cells and updates adjacency
    private bool MergeCells(Dictionary<Vector2, List<Vector2>> cells, Vector2 site1, Vector2 site2) {
        if (site1 == site2) { Debug.LogError("MergeCells: Cannot merge a site with itself.", this); return false; }

        // --- Pre-checks ---
        // Ensure sites and their polygons exist in the current 'cells' dictionary
        int siteIndex1 = -1;
        int siteIndex2 = -1;

        // Check if sites exist in cells dict...
        if (!cells.TryGetValue(site1, out List<Vector2> poly1) || poly1 == null || poly1.Count < 3) { // Also use TryGetValue here for consistency
            Debug.LogError($"MergeCells: Site1 ({site1}) polygon is missing, null, or has < 3 vertices in the 'cells' dictionary. Cannot merge.");
            return false;
        }
        if (!cells.TryGetValue(site2, out List<Vector2> poly2) || poly2 == null || poly2.Count < 3) { // Also use TryGetValue here
            if(enableVerboseLogging) Debug.Log($"MergeCells: Site2 ({site2}) polygon is missing or invalid in 'cells'. Likely already merged away. Skipping merge into {site1}.");
            return false;
        }

        // Check if sites exist in index map and assign indices using TryGetValue
        if (!sitePosToIndex.TryGetValue(site1, out siteIndex1) || !sitePosToIndex.TryGetValue(site2, out siteIndex2)) {
            Debug.LogError($"MergeCells: Site-to-index mapping missing for {site1} or {site2}.");
            return false;
        }
         // Ensure Delaunay and circumcenter data (needed for finding shared edge) are available
         if (currentDelaunator == null || calculatedCircumcenters == null) {
               Debug.LogError("MergeCells requires valid Delaunator and Circumcenter data from the main calculation step.", this);
               return false;
         }

        // --- 1. Find the shared Voronoi edge vertices ---
        // This uses the Delaunay triangulation to find the two circumcenters defining the edge between site1 and site2.
        if (!FindSharedVoronoiVertices(siteIndex1, siteIndex2, currentDelaunator, calculatedCircumcenters, out Vector2? sharedV1Nullable, out Vector2? sharedV2Nullable)) {
            // Could not find a valid shared edge based on the Delaunay triangulation.
            // This might indicate they aren't truly adjacent according to the triangulation,
            // or one of the defining circumcenters was null/invalid.
            Debug.LogError($"MergeCells: Could not find valid shared Voronoi edge between sites {siteIndex1} ({site1}) and {siteIndex2} ({site2}). Check adjacency calculation or if they are truly Delaunay neighbors.", this);

            // Optional: Log adjacency list for debugging
            if (realSiteAdjacency.TryGetValue(site1, out var adjList_tmp)) {
                 bool areAdjacent = adjList_tmp.Contains(site2);
                 string neighborIndices = string.Join(", ", adjList_tmp.Select(n => sitePosToIndex.ContainsKey(n) ? sitePosToIndex[n].ToString() : "N/A"));
                 Debug.Log($"Site {siteIndex1} ({site1}) adjacency check: Contains site {siteIndex2} ({site2})? {areAdjacent}. Neighbors: [{neighborIndices}]");
            } else { Debug.Log($"Site {siteIndex1} ({site1}) not found in realSiteAdjacency."); }
            return false;
        }

        // Ensure the returned vertices are not null
        if (!sharedV1Nullable.HasValue || !sharedV2Nullable.HasValue) {
             Debug.LogError($"MergeCells: Shared Voronoi vertices search returned true, but one or both vertices are null for sites {siteIndex1} ({site1}) and {siteIndex2} ({site2}). This indicates an issue with circumcenter validity.", this);
             return false;
        }
        Vector2 sharedV1 = sharedV1Nullable.Value;
        Vector2 sharedV2 = sharedV2Nullable.Value;


        // --- 2. Find the indices of these shared vertices in each polygon's list ---
        int idx1A = FindVertexIndex(poly1, sharedV1); int idx1B = FindVertexIndex(poly1, sharedV2);
        int idx2A = FindVertexIndex(poly2, sharedV1); int idx2B = FindVertexIndex(poly2, sharedV2);

        // Check if all indices were found successfully
        if (idx1A < 0 || idx1B < 0 || idx2A < 0 || idx2B < 0) {
            Debug.LogError($"MergeCells: Could not find one or more shared vertices V1({sharedV1}), V2({sharedV2}) in polygon lists. Poly1 indices: A={idx1A}, B={idx1B}. Poly2 indices: A={idx2A}, B={idx2B}", this);
             // Optional: Log polygons for debugging
             if(enableVerboseLogging) {
                  Debug.Log($"Poly1 ({siteIndex1}): Count={poly1.Count}, Vertices=[{string.Join(", ", poly1)}]");
                  Debug.Log($"Poly2 ({siteIndex2}): Count={poly2.Count}, Vertices=[{string.Join(", ", poly2)}]");
             }
            return false;
        }


        // --- 3. Ensure the shared edge direction is opposite in the two polygons ---
        // Assumes polygons are ordered consistently (e.g., clockwise).
        // Check if moving from index A to the next index in poly1 leads to index B.
        bool poly1OrderIsAtoB = GetLoopedIndex(idx1A + 1, poly1.Count) == idx1B;
        // Check if moving from index B to the next index in poly2 leads to index A.
        bool poly2OrderIsBtoA = GetLoopedIndex(idx2B + 1, poly2.Count) == idx2A;

        // If the orders don't match (one is A->B, the other should be B->A along the shared edge),
        // the merge logic might fail. This indicates inconsistent polygon winding or errors.
        if (poly1OrderIsAtoB != poly2OrderIsBtoA) {
             Debug.LogError($"MergeCells: Shared edge vertex order mismatch between polygons. Poly1({idx1A}->{idx1B}) order A->B is {poly1OrderIsAtoB}. Poly2({idx2B}->{idx2A}) order B->A is {poly2OrderIsBtoA}. Cannot merge reliably.", this);
             // Winding order might be inconsistent.
             if(enableVerboseLogging) Debug.Log($"Poly1 Area: {CalculateSignedArea(poly1)}, Poly2 Area: {CalculateSignedArea(poly2)}");
             return false;
        }


        // --- 4. Standardize traversal direction for merging ---
        // We want to traverse poly1 from B back to A, then poly2 from A back to B (excluding shared points).
        // If poly1 was A->B, we use indices (idx1B ... idx1A).
        // If poly1 was B->A, we swap A and B indices so we still use (idx1B ... idx1A) after swap.
        if (!poly1OrderIsAtoB) { // If poly1 order was B->A
             // Swap indices for poly1
             int temp = idx1A; idx1A = idx1B; idx1B = temp;
             // Swap indices for poly2 to maintain consistency (if poly1 was B->A, poly2 must be A->B)
             temp = idx2A; idx2A = idx2B; idx2B = temp;
             if(enableVerboseLogging) Debug.Log("MergeCells: Swapped indices to standardize merge traversal (Poly1 B->A, Poly2 A->B initially).");
        }
        // Now, poly1 traversal is from idx1B to idx1A (exclusive of shared edge).
        // And poly2 traversal is from idx2A to idx2B (exclusive of shared edge).


        // --- 5. Construct the merged polygon ---
        List<Vector2> mergedPoly = new List<Vector2>(poly1.Count + poly2.Count); // Pre-allocate roughly
        int safetyCounter = 0; int maxIter = poly1.Count + poly2.Count + 10; // Generous safety limit

        // Add vertices from poly1 (from B up to A, excluding the shared edge A->B)
        int currentIdx = idx1B;
        while (safetyCounter < maxIter) {
            mergedPoly.Add(poly1[currentIdx]);
            if (currentIdx == idx1A) break; // Stop when we reach the start of the shared edge A
            currentIdx = GetLoopedIndex(currentIdx + 1, poly1.Count); // Move to next vertex in poly1
            safetyCounter++;
        }
         if (safetyCounter >= maxIter) { Debug.LogError($"Merge loop poly1 exceeded max iterations ({maxIter}). Site1: {siteIndex1}, Site2: {siteIndex2}", this); return false; }

        // Add vertices from poly2 (from A up to B, excluding the shared edge B->A)
        currentIdx = GetLoopedIndex(idx2A + 1, poly2.Count); // Start from the vertex *after* A in poly2
        while (safetyCounter < maxIter) {
             // Check if we've reached the end of the shared edge B in poly2
             if (currentIdx == idx2B) break;

             // Add vertex, optionally checking for duplicates against the very first added vertex
             // (from poly1's idx1B) to prevent adding poly2's idx2A if it's identical.
             // CleanPolygon should handle most duplicates, but this can be a pre-check.
             if (mergedPoly.Count == 0 || Vector2.SqrMagnitude(mergedPoly[mergedPoly.Count - 1] - poly2[currentIdx]) > Epsilon * Epsilon)
             {
                  // Add only if not a duplicate of the last added vertex
                  mergedPoly.Add(poly2[currentIdx]);
             }
             else { if(enableVerboseLogging) Debug.LogWarning($"Merge skipped duplicate vertex from poly2 loop: {poly2[currentIdx]}"); }


             currentIdx = GetLoopedIndex(currentIdx + 1, poly2.Count); // Move to next vertex in poly2
             safetyCounter++;
        }
        if (safetyCounter >= maxIter) { Debug.LogError($"Merge loop poly2 exceeded max iterations ({maxIter}). Site1: {siteIndex1}, Site2: {siteIndex2}", this); return false; }


        // --- 6. Clean, reorder, and update dictionary ---
        List<Vector2> cleanedMerged = CleanPolygon(mergedPoly);
        if (cleanedMerged.Count < 3) {
            // Merging resulted in a degenerate polygon
            Debug.LogError($"MergeCells: Merged polygon between {siteIndex1} and {siteIndex2} resulted in < 3 vertices after cleaning ({cleanedMerged.Count}). Cannot update cell.", this);
            // Optional: Log vertices before/after cleaning
             if(enableVerboseLogging) {
                 Debug.Log($"Merged Raw ({mergedPoly.Count}): [{string.Join(", ", mergedPoly)}]");
                 Debug.Log($"Merged Cleaned ({cleanedMerged.Count}): [{string.Join(", ", cleanedMerged)}]");
             }
            return false; // Indicate merge failure
        }

        // Update site1's polygon in the cells dictionary and remove site2
        cells[site1] = OrderVerticesClockwise(cleanedMerged); // Ensure correct winding order
        cells.Remove(site2); // Remove the merged cell


        // --- 7. Update Adjacency Information ---
        // Site2 is gone. Its former neighbors (excluding site1) need to become neighbors of site1.
        // Site1 loses site2 as a neighbor. Neighbors of site1 remain otherwise.

        // Get adjacency lists (handle cases where they might not exist, though they should)
        realSiteAdjacency.TryGetValue(site1, out List<Vector2> adjList1); // Null if key not found
        realSiteAdjacency.TryGetValue(site2, out List<Vector2> adjList2); // Null if key not found

        // Remove the direct link between site1 and site2
        adjList1?.Remove(site2); // Safe navigation: only call Remove if adjList1 is not null
        // adjList2?.Remove(site1); // Not strictly necessary as adjList2 will be removed entirely

        // Transfer site2's neighbors to site1
        if (adjList1 != null && adjList2 != null) { // Proceed only if both lists were found
            foreach (Vector2 neighborOf2 in adjList2) { // Iterate over site2's neighbors
                if (neighborOf2 != site1) { // Don't add site1 to its own list
                    // Add neighborOf2 to site1's list if not already present
                    if (!adjList1.Contains(neighborOf2)) {
                        adjList1.Add(neighborOf2);
                    }

                    // Update the neighbor's list: Remove site2, Add site1 (if necessary)
                    if (realSiteAdjacency.TryGetValue(neighborOf2, out var adjNeighbor)) { // Get the neighbor's adjacency list
                        adjNeighbor.Remove(site2); // Remove the link to the now-gone site2
                        if (!adjNeighbor.Contains(site1)) { // Add the link to site1 if it doesn't exist
                            adjNeighbor.Add(site1);
                        }
                    } else {
                        // This neighbor of site2 doesn't have an entry in realSiteAdjacency.
                        // This could happen if neighborOf2 is a fake site or was merged away earlier.
                         if (enableVerboseLogging) Debug.LogWarning($"Merge Adjacency Update: Neighbor '{neighborOf2}' of merged site '{site2}' not found in realSiteAdjacency. Cannot update its list.");
                    }
                }
            }
        } else {
             if (enableVerboseLogging) {
                  if (adjList1 == null) Debug.LogWarning($"Merge Adjacency Update: Adjacency list for site1 ({site1}) not found.");
                  if (adjList2 == null) Debug.LogWarning($"Merge Adjacency Update: Adjacency list for site2 ({site2}) not found.");
             }
        }

        // Remove site2 from the adjacency dictionary entirely
        realSiteAdjacency.Remove(site2);

        if (enableVerboseLogging) Debug.Log($"Internal Merge: Site {siteIndex2} ({site2}) merged into {siteIndex1} ({site1}). New vertex count: {cells[site1].Count}. Adjacency updated.");
        return true; // Indicate successful merge
    }



    // Finds the two Voronoi vertices (circumcenters) that form the edge between two adjacent Delaunay sites
    private bool FindSharedVoronoiVertices(int siteIndex1, int siteIndex2, Delaunator delaunator, Vector2?[] circumcenters, out Vector2? v1, out Vector2? v2) {
        v1 = null; v2 = null;
        if (delaunator == null || circumcenters == null) {
             Debug.LogError("FindSharedVoronoiVertices: Delaunator or circumcenters data is null.", this);
             return false;
        }

        // Iterate through all half-edges in the Delaunay triangulation
        for (int e = 0; e < delaunator.Triangles.Length; e++) {
            // Get the start and end point indices of the current Delaunay edge 'e'
            int pStart = delaunator.Triangles[e];
            int pEnd = delaunator.Triangles[NextHalfedgeIndex(e)];

            // Check if this edge connects siteIndex1 and siteIndex2 (in either direction)
            if ((pStart == siteIndex1 && pEnd == siteIndex2) || (pStart == siteIndex2 && pEnd == siteIndex1)) {
                // Found the Delaunay edge connecting the two sites.
                // The shared Voronoi edge vertices are the circumcenters of the two triangles sharing this Delaunay edge.

                // Get the triangle ID containing the current half-edge 'e'
                int triangleId1 = e / 3;
                // Get the half-edge opposite to 'e'
                int oppositeEdge = delaunator.Halfedges[e];
                // Get the triangle ID on the other side of the edge (-1 if it's a hull edge)
                int triangleId2 = (oppositeEdge != -1) ? oppositeEdge / 3 : -1;

                // Retrieve the circumcenters for these two triangles
                Vector2? c1 = GetCircumcenterFromTriangleId(triangleId1, circumcenters);
                Vector2? c2 = (triangleId2 != -1) ? GetCircumcenterFromTriangleId(triangleId2, circumcenters) : null;

                // Check if both circumcenters are valid
                if (c1.HasValue && c2.HasValue) {
                    // Found two valid circumcenters defining the shared Voronoi edge
                    v1 = c1;
                    v2 = c2;
                    return true; // Success
                } else {
                     // Found the edge, but at least one circumcenter is null (likely due to collinear points or bounds check)
                     // The Voronoi edge is effectively open or degenerate at this end.
                     if (enableVerboseLogging) {
                          string reason = "";
                          if (!c1.HasValue) reason += $"Circumcenter C1(T{triangleId1}) is null. ";
                          if (triangleId2 == -1) reason += "Edge is on hull (no C2).";
                          else if (!c2.HasValue) reason += $"Circumcenter C2(T{triangleId2}) is null.";
                          Debug.LogWarning($"FindSharedVoronoiVertices: Found Delaunay edge {siteIndex1}-{siteIndex2}, but Voronoi edge is incomplete. Reason: {reason}");
                     }
                    // We found the edge but it's not well-defined by two vertices, so return false for merging purposes.
                    return false;
                }
            }
        }

        // If the loop completes without finding an edge connecting the two sites
        // if (enableVerboseLogging) Debug.Log($"FindSharedVoronoiVertices: No Delaunay edge found connecting sites {siteIndex1} and {siteIndex2}. They might not be adjacent."); // Reduce log spam
        return false; // No connecting edge found
    }

    // Safely retrieves a circumcenter from the array by triangle ID
    private Vector2? GetCircumcenterFromTriangleId(int triangleId, Vector2?[] circumcenters) {
        if (circumcenters == null) {
             Debug.LogError("GetCircumcenterFromTriangleId: Circumcenters array is null!", this);
             return null;
         }
        // Check for valid index range
        if (triangleId < 0 || triangleId >= circumcenters.Length) {
             Debug.LogError($"GetCircumcenterFromTriangleId: Invalid triangleId {triangleId}. Array size: {circumcenters.Length}", this);
             return null;
         }
        // Return the (potentially null) circumcenter value
        return circumcenters[triangleId];
    }


    // Finds the index of a vertex in a list using proximity check
    private int FindVertexIndex(List<Vector2> polygon, Vector2 vertex) {
        float minSqrDist = Epsilon * Epsilon; // Use squared epsilon for comparison
        int bestIndex = -1;

        if (polygon == null) return -1; // Handle null polygon list

        for (int i = 0; i < polygon.Count; i++) {
            float sqrDist = Vector2.SqrMagnitude(polygon[i] - vertex);
            // Check if this vertex is closer than the current best match
            if (sqrDist < minSqrDist) {
                 minSqrDist = sqrDist; // Update minimum distance found
                 bestIndex = i;       // Update best index found
                 // Optimization: If distance is extremely small, assume exact match and break early
                 if (sqrDist < 1e-12f) break;
            }
        }
        // Return the index of the closest vertex found, or -1 if no vertex was within epsilon distance
        return bestIndex;
    }

    #endregion

    #region Mesh Generation & Triangulation

    // Creates the fill mesh for a Voronoi cell
    void CreateCellFillMesh(Vector2 site, StarSystemData star, List<Vector2> polygonVertices, Transform parent) {
         // Pre-checks (already done in ApplyQueuedMergesAndRegenerate, but double-check)
         if (polygonVertices == null || polygonVertices.Count < 3) {
             Debug.LogWarning($"Skipping Fill Mesh for {star.systemName} (Site: {site}): Not enough vertices ({polygonVertices?.Count ?? 0}) provided.", this);
             return;
         }
         if (star == null) {
             Debug.LogError($"Skipping Fill Mesh for site {site}: Corresponding StarSystemData is null.", this);
             return;
         }

         // Create GameObject for the fill
         GameObject cellFillGO = new GameObject($"CellFill_{star.systemName}");
         cellFillGO.transform.SetParent(parent, false); // Set parent without affecting world position
         cellFillGO.transform.localPosition = Vector3.zero; // Ensure local position is zero
         generatedOverlayObjects.Add(cellFillGO); // Add to list for cleanup
         starToFillObjectMap[star] = cellFillGO; // Map star to this GameObject

         // Add Mesh components
         MeshFilter meshFilter = cellFillGO.AddComponent<MeshFilter>();
         MeshRenderer meshRenderer = cellFillGO.AddComponent<MeshRenderer>();

        // --- Material Handling ---
        Material materialInstance;
        // Try to get existing material for this site (Vector2)
        if (!regionMaterials.TryGetValue(site, out materialInstance) || materialInstance == null)
        {
            // Create new material instance if not found or if the stored one became null
            if(materialInstance == null && regionMaterials.ContainsKey(site)) {
                if(enableVerboseLogging) Debug.Log($"Material for site {site} was null, creating new one.");
            }

            materialInstance = new Material(cellFillMaterialTemplate); // Instantiate from template
            // --- Assign Random Color (Example) ---
            Color randomColor = Color.HSVToRGB(
                Random.value,                 // Hue (0..1)
                Random.Range(0.6f, 0.9f),     // Saturation (make it vibrant)
                Random.Range(0.7f, 1.0f)      // Value (make it bright)
            );
            randomColor.a = cellFillMaterialTemplate.color.a; // Use alpha from template (or set explicitly, e.g., 0.5f)
            // --- Set Color Property ---
            if (!string.IsNullOrEmpty(fillColorPropertyName)) {
                 if (materialInstance.HasProperty(fillColorPropertyName)) {
                     materialInstance.SetColor(fillColorPropertyName, randomColor);
                 } else {
                      LogWarningOnceStatic($"Fill Color Property Name '{fillColorPropertyName}' not found in template material shader '{cellFillMaterialTemplate.shader.name}' for {star.systemName}.", this);
                 }
            } else {
                 // Warning already logged in Start/Validate
            }
            materialInstance.name = $"RegionMat_{star.systemName}_{site.GetHashCode()}"; // Unique name
            regionMaterials[site] = materialInstance; // Store material by site Vector2
            if (enableVerboseLogging) Debug.Log($"Created new material '{materialInstance.name}' for site {site} ({star.systemName}) with color {randomColor}");
        } else {
             // Reuse existing material for this site
             if (enableVerboseLogging) Debug.Log($"Reusing existing material '{materialInstance.name}' for site {site} ({star.systemName})");
        }
        meshRenderer.material = materialInstance; // Assign the material
        // --- End Material Handling ---

        // Set rendering options (optional)
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        // Create Mesh data
        Mesh mesh = new Mesh { name = $"VoronoiFill_{star.systemName}" };

        // Convert 2D polygon vertices to 3D, applying Y offset
        Vector3[] vertices3D = new Vector3[polygonVertices.Count];
        for (int i = 0; i < polygonVertices.Count; i++) {
            vertices3D[i] = new Vector3(polygonVertices[i].x, yOffset, polygonVertices[i].y); // Map Z to Y
        }
        mesh.vertices = vertices3D;

        // Triangulate the polygon
        int[] triangles = TriangulateEarClipping(polygonVertices);
        if (triangles == null || triangles.Length == 0) {
             // Triangulation failed
             if (polygonVertices.Count >= 3) { // Only log error if polygon was actually valid
                 Debug.LogError($"Ear Clipping triangulation failed for cell {star.systemName} (Site: {site}). Vertex count: {polygonVertices.Count}. Polygon might be self-intersecting or invalid.", cellFillGO);
             }
             // Clean up the created GameObject and mappings if mesh generation fails
             starToFillObjectMap.Remove(star);
             generatedOverlayObjects.Remove(cellFillGO);
             DestroyImmediateSafe(cellFillGO);
             // Note: Material is NOT destroyed here, relies on ClearOverlay for full cleanup
             return; // Stop processing this mesh
        }
        mesh.triangles = triangles;

        // Finalize mesh (Normals are important for lighting/shading, Bounds for culling)
        try {
            mesh.RecalculateNormals();
        } catch (System.Exception ex) {
            // Catch potential errors during normal calculation (e.g., degenerate triangles)
            Debug.LogWarning($"RecalculateNormals failed for fill mesh {star.systemName}: {ex.Message}. Mesh might not shade correctly.", cellFillGO);
        }
        mesh.RecalculateBounds();
        meshFilter.mesh = mesh; // Assign the completed mesh
    }

    // Creates the border mesh for a Voronoi cell
    void CreateCellBorderMesh(StarSystemData star, List<Vector2> polygonVertices, Transform parent) {
        if (polygonVertices == null || polygonVertices.Count < 2) { // Need at least 2 vertices for a line segment
             // if (enableVerboseLogging) Debug.Log($"Skipping Border Mesh for {star.systemName}: Not enough vertices ({polygonVertices?.Count ?? 0}).");
             return;
        }
         if (star == null) {
             Debug.LogError($"Skipping Border Mesh: Corresponding StarSystemData is null.", this);
             return;
         }
         if (cellBorderMaterial == null) {
              Debug.LogError($"Skipping Border Mesh for {star.systemName}: Cell Border Material is not assigned.", this);
              return;
         }

        // Create GameObject for the border
        GameObject cellBorderGO = new GameObject($"CellBorder_{star.systemName}");
        cellBorderGO.transform.SetParent(parent, false);
        cellBorderGO.transform.localPosition = Vector3.zero;
        generatedOverlayObjects.Add(cellBorderGO);
        starToBorderObjectMap[star] = cellBorderGO; // Map star to this GameObject

        // Add Mesh components
        MeshFilter meshFilter = cellBorderGO.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = cellBorderGO.AddComponent<MeshRenderer>();
        meshRenderer.material = cellBorderMaterial; // Assign border material
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        // Prepare lists for mesh data
        Mesh mesh = new Mesh { name = $"VoronoiBorder_{star.systemName}" };
        List<Vector3> borderVertices = new List<Vector3>();
        List<int> borderTriangles = new List<int>();
        int baseIndex = 0; // Index offset for adding triangles for each segment
        float borderYOffset = yOffset - 0.001f; // Position border slightly below fill

        // Generate border segments (quads) for each edge of the polygon
        for (int i = 0; i < polygonVertices.Count; i++) {
            Vector2 p1_2D = polygonVertices[i];
            Vector2 p2_2D = polygonVertices[GetLoopedIndex(i + 1, polygonVertices.Count)]; // Get next vertex, wrapping around

            // Skip degenerate segments
            if (Vector2.SqrMagnitude(p1_2D - p2_2D) < Epsilon * Epsilon) continue;

            // Convert 2D points to 3D points on the desired plane
            Vector3 p1 = new Vector3(p1_2D.x, borderYOffset, p1_2D.y);
            Vector3 p2 = new Vector3(p2_2D.x, borderYOffset, p2_2D.y);

            // Calculate direction and perpendicular normal for the border width
            Vector3 direction = (p2 - p1).normalized;
            Vector3 normal = Vector3.Cross(direction, Vector3.up).normalized; // Perpendicular in XZ plane

            // Calculate the four vertices of the quad for this border segment
            float halfWidth = borderWidth / 2f;
            Vector3 v0 = p1 - normal * halfWidth; // Bottom-left (relative to direction)
            Vector3 v1 = p1 + normal * halfWidth; // Top-left
            Vector3 v2 = p2 - normal * halfWidth; // Bottom-right
            Vector3 v3 = p2 + normal * halfWidth; // Top-right

            // Add vertices to the list
            borderVertices.AddRange(new Vector3[] { v0, v1, v2, v3 });

            // Add triangles for the quad (two triangles: 0-1-2 and 1-3-2)
            borderTriangles.Add(baseIndex + 0); borderTriangles.Add(baseIndex + 1); borderTriangles.Add(baseIndex + 2);
            borderTriangles.Add(baseIndex + 1); borderTriangles.Add(baseIndex + 3); borderTriangles.Add(baseIndex + 2);

            baseIndex += 4; // Increment base index for the next quad
        }

        // Check if any border geometry was actually generated
        if (borderVertices.Count == 0) {
             // if (enableVerboseLogging) Debug.Log($"No border segments generated for {star.systemName}.");
             // Clean up the empty GameObject
             starToBorderObjectMap.Remove(star);
             generatedOverlayObjects.Remove(cellBorderGO);
             DestroyImmediateSafe(cellBorderGO);
             return; // Nothing more to do
        }

        // Assign data to the mesh
        mesh.vertices = borderVertices.ToArray();
        mesh.triangles = borderTriangles.ToArray();

        // Finalize mesh
        try {
             mesh.RecalculateNormals(); // Normals point upwards (Vector3.up)
         } catch (System.Exception ex) {
              Debug.LogWarning($"RecalculateNormals failed for border mesh {star.systemName}: {ex.Message}.", cellBorderGO);
          }
        mesh.RecalculateBounds();
        meshFilter.mesh = mesh; // Assign the completed mesh
    }

    // Simple Ear Clipping triangulation algorithm for convex/non-convex polygons
    private int[] TriangulateEarClipping(List<Vector2> polygonVertices) {
        if (polygonVertices == null || polygonVertices.Count < 3) {
             // if (enableVerboseLogging) Debug.Log("TriangulateEarClipping: Input polygon has < 3 vertices.");
             return null; // Cannot triangulate
         }
         if (polygonVertices.Count == 3) {
             // Already a triangle
             return new int[] { 0, 1, 2 }; // Assuming clockwise or correct winding already
         }

        List<int> triangles = new List<int>(); // Stores the resulting triangle indices
        // List of vertex indices currently part of the polygon being clipped
        List<int> activeIndices = new List<int>(polygonVertices.Count);
        for (int i = 0; i < polygonVertices.Count; i++) { activeIndices.Add(i); }

        // Determine winding order (needed for convexity check)
        bool isClockwise = CalculateSignedArea(polygonVertices) < 0;
        if (enableVerboseLogging && CalculateSignedArea(polygonVertices) == 0) {
            Debug.LogWarning($"TriangulateEarClipping: Polygon area is zero, vertices may be collinear. Result may be incorrect.");
        }

        int loopSafetyCounter = 0;
        // Max loops: Roughly O(n^2) in worst case, add buffer.
        int maxLoops = activeIndices.Count * activeIndices.Count * 2 + 100;
        int currentIndex = 0; // Start checking from the first vertex in the active list

        // Keep clipping ears until only 3 vertices (one triangle) remain
        while (activeIndices.Count > 3 && loopSafetyCounter < maxLoops) {
            loopSafetyCounter++;

            // Get indices for the potential ear triangle (prev, current, next) in the *active* list
            int prevListIndex = GetLoopedIndex(currentIndex - 1, activeIndices.Count);
            int currListIndex = currentIndex;
            int nextListIndex = GetLoopedIndex(currentIndex + 1, activeIndices.Count);

            // Get the original indices from the input polygonVertices list
            int prevOriginalIndex = activeIndices[prevListIndex];
            int currOriginalIndex = activeIndices[currListIndex];
            int nextOriginalIndex = activeIndices[nextListIndex];

            // Get the actual vertex positions
            Vector2 prevPoint = polygonVertices[prevOriginalIndex];
            Vector2 currPoint = polygonVertices[currOriginalIndex];
            Vector2 nextPoint = polygonVertices[nextOriginalIndex];

            // --- Check 1: Is the vertex at currOriginalIndex convex? ---
            if (IsVertexConvex(prevPoint, currPoint, nextPoint, isClockwise)) {

                // --- Check 2: Does the triangle (prev, curr, next) contain any *other* active vertex? ---
                bool isEar = true; // Assume it's an ear initially
                for (int i = 0; i < activeIndices.Count; i++) {
                    int testListIndex = i;
                    // Skip the vertices forming the potential ear triangle itself
                    if (testListIndex == prevListIndex || testListIndex == currListIndex || testListIndex == nextListIndex) continue;

                    int testOriginalIndex = activeIndices[testListIndex];
                    Vector2 testPoint = polygonVertices[testOriginalIndex];

                    // Use a robust point-in-triangle check
                    // allowOnEdge=false is stricter, true allows points on the edge (can cause issues with near-collinear)
                    if (IsPointInTriangle(testPoint, prevPoint, currPoint, nextPoint, allowOnEdge: false)) {
                        isEar = false; // Found another vertex inside the potential ear
                        break;        // No need to check further vertices
                    }
                }

                // --- If it's a valid ear (convex and contains no other points) ---
                if (isEar) {
                    // Add the triangle (indices must be in correct winding order)
                    // If original polygon was clockwise, add P->C->N. If CCW, add P->N->C.
                    // However, Unity typically uses clockwise, so P, C, N should work if IsVertexConvex is correct.
                    triangles.Add(prevOriginalIndex);
                    triangles.Add(currOriginalIndex);
                    triangles.Add(nextOriginalIndex);

                    // Remove the ear's tip (current vertex) from the active list
                    activeIndices.RemoveAt(currListIndex);

                    // Adjust current index and reset safety counter as progress was made
                    // No need to advance index, the vertex at the *new* currListIndex should be checked next.
                    currentIndex = currentIndex % activeIndices.Count; // Ensure index stays valid
                    loopSafetyCounter = 0; // Reset safety counter on successful clip
                    continue; // Go to next iteration immediately to check the new polygon shape
                }
                // else: It wasn't a valid ear (either reflex or contained another point)
            }
            // else: Vertex was reflex (not convex)

            // Move to the next vertex in the active list to check if *it* forms an ear
            currentIndex = (currentIndex + 1) % activeIndices.Count;

        } // End while loop

        // After loop: should have 3 vertices left if successful
        if (activeIndices.Count == 3) {
            // Add the final remaining triangle
            triangles.Add(activeIndices[0]);
            triangles.Add(activeIndices[1]);
            triangles.Add(activeIndices[2]);
        } else if (loopSafetyCounter >= maxLoops) {
            // Triangulation failed due to exceeding max iterations (likely complex/invalid polygon)
            Debug.LogError($"Ear Clipping failed: Exceeded max loop iterations ({maxLoops}). Remaining vertices: {activeIndices.Count}. Polygon might be complex or self-intersecting.", this);
            if(enableVerboseLogging) Debug.Log($"Failed Polygon Vertices: [{string.Join("), (", polygonVertices)}]");
            return null; // Indicate failure
        } else {
            // Triangulation failed for other reasons (polygon decomposed unexpectedly)
            Debug.LogError($"Ear Clipping failed: Unexpected number of vertices remaining ({activeIndices.Count}). Polygon might be invalid.", this);
             if(enableVerboseLogging) Debug.Log($"Failed Polygon Vertices: [{string.Join("), (", polygonVertices)}]");
            return null; // Indicate failure
        }

        return triangles.ToArray(); // Return the list of triangle indices
    }


    // Helper for looping indices in a list
    private int GetLoopedIndex(int index, int listSize) {
         if (listSize <= 0) return 0; // Avoid division by zero or negative modulo
         // Handles positive and negative indices correctly
         return (index % listSize + listSize) % listSize;
    }

    // Calculates the signed area of a 2D polygon using the Shoelace formula
    // Positive area indicates counter-clockwise winding, negative indicates clockwise (in standard math coords)
    // Note: Unity's screen/UI coordinates might invert Y, affecting interpretation if used directly there.
    private float CalculateSignedArea(List<Vector2> polygon) {
        if (polygon == null || polygon.Count < 3) return 0f; // Not a polygon

        float area = 0f;
        for (int i = 0; i < polygon.Count; i++) {
            Vector2 p1 = polygon[i];
            Vector2 p2 = polygon[GetLoopedIndex(i + 1, polygon.Count)]; // Next vertex with wrap-around
            // Shoelace formula component: (x1*y2 - x2*y1)
            area += (p1.x * p2.y - p2.x * p1.y);
        }
        return area / 2.0f; // Final area
    }

    // Checks if a vertex is convex based on the cross product of edge vectors and polygon winding
    private bool IsVertexConvex(Vector2 prev, Vector2 curr, Vector2 next, bool polygonIsClockwise) {
       // Calculate the 2D cross product (Z component of 3D cross product)
       // Vector A = curr - prev
       // Vector B = next - curr
       // Cross product Z = A.x * B.y - A.y * B.x
       float crossProductZ = (curr.x - prev.x) * (next.y - curr.y) - (curr.y - prev.y) * (next.x - curr.x);

       // Use a small tolerance for floating point comparisons
       float tolerance = 1e-5f;

       // Determine convexity based on winding order
       if (polygonIsClockwise) {
           // For clockwise polygons, convex vertices have cross product <= 0 (or slightly positive within tolerance)
           return crossProductZ <= tolerance;
       } else {
           // For counter-clockwise polygons, convex vertices have cross product >= 0 (or slightly negative within tolerance)
           return crossProductZ >= -tolerance;
       }
    }

    // Barycentric coordinate check for point-in-triangle
    private bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, bool allowOnEdge = false) {
        // Compute vectors
        Vector2 v0 = c - a;
        Vector2 v1 = b - a;
        Vector2 v2 = p - a;

        // Compute dot products
        float dot00 = Vector2.Dot(v0, v0);
        float dot01 = Vector2.Dot(v0, v1);
        float dot02 = Vector2.Dot(v0, v2);
        float dot11 = Vector2.Dot(v1, v1);
        float dot12 = Vector2.Dot(v1, v2);

        // Compute barycentric coordinates
        float denominator = (dot00 * dot11 - dot01 * dot01);

        // Check for degenerate triangle (collinear vertices)
        if (Mathf.Abs(denominator) < Epsilon * Epsilon) return false; // Cannot be inside a degenerate triangle

        float invDenom = 1.0f / denominator;
        float u = (dot11 * dot02 - dot01 * dot12) * invDenom; // Barycentric coord for v0 (AC edge)
        float v = (dot00 * dot12 - dot01 * dot02) * invDenom; // Barycentric coord for v1 (AB edge)

        // Check if point is within triangle bounds
        float lowerBound = allowOnEdge ? -Epsilon : Epsilon; // Use tolerance near 0
        float upperBound = allowOnEdge ? (1.0f + Epsilon) : (1.0f - Epsilon); // Use tolerance near 1

        // Point is inside if u >= 0, v >= 0, and u + v <= 1 (within tolerances)
        return (u >= lowerBound) && (v >= lowerBound) && (u + v <= upperBound);
    }


    // Safely destroys Objects (GameObjects, Materials, Meshes) in both Play and Editor modes
    private void DestroyImmediateSafe(Object obj) {
         if (obj == null) return; // Nothing to destroy

         if (Application.isPlaying) {
             Destroy(obj); // Use standard Destroy in Play mode
         }
         else {
             DestroyImmediate(obj); // Use DestroyImmediate in Editor mode
         }
    }

    // Static helper method for logging warnings only once per message content
    private static void LogWarningOnceStatic(string message, Object context = null) {
        // Use the static loggedWarnings HashSet defined in this class
        if (loggedWarnings.Add(message)) { // .Add returns true if the item was new
            Debug.LogWarning(message, context); // Log only if it's a new message
        }
    }
    #endregion

    #region Helper Struct (Point) for DelaunatorSharp
    // Simple struct implementing IPoint for DelaunatorSharp library
    public struct Point : IPoint {
        public double X { get; set; } // Delaunator uses double precision
        public double Y { get; set; }
        public Point(double x, double y) { X = x; Y = y; }
    }
    #endregion

    #region Optional: Gizmos for Debugging
    #if UNITY_EDITOR
    void OnDrawGizmos() {
        if (!enableVerboseLogging) return; // Only draw gizmos if verbose logging is enabled

        // --- Draw Fake Point Circle ---
        if (calculatedFakePointRadius > 0) {
            Gizmos.color = Color.magenta;
            int segments = 60;
            Vector3 prevPoint = Vector3.zero;
            bool first = true;
            for (int i = 0; i <= segments; i++) {
                float angle = i * (2 * Mathf.PI / segments);
                // Draw on the XZ plane at the specified yOffset
                Vector3 currentPoint = new Vector3(
                    calculatedFakePointRadius * Mathf.Cos(angle),
                    yOffset,
                    calculatedFakePointRadius * Mathf.Sin(angle)
                );
                if (!first) { Gizmos.DrawLine(prevPoint, currentPoint); }
                prevPoint = currentPoint;
                first = false;
            }
        }

        // --- Draw Circumcenter Check Bounds ---
        Gizmos.color = Color.yellow;
        Vector3 c0 = new Vector3(circumcenterCheckBounds.xMin, yOffset, circumcenterCheckBounds.yMin);
        Vector3 c1 = new Vector3(circumcenterCheckBounds.xMax, yOffset, circumcenterCheckBounds.yMin);
        Vector3 c2 = new Vector3(circumcenterCheckBounds.xMax, yOffset, circumcenterCheckBounds.yMax);
        Vector3 c3 = new Vector3(circumcenterCheckBounds.xMin, yOffset, circumcenterCheckBounds.yMax);
        Gizmos.DrawLine(c0, c1); Gizmos.DrawLine(c1, c2); Gizmos.DrawLine(c2, c3); Gizmos.DrawLine(c3, c0);

        // --- Draw Generated Mesh Wireframes ---
        // Use the maps to draw wireframes only for active objects
        Gizmos.color = Color.cyan;
        foreach (var kvp in starToFillObjectMap) {
             GameObject obj = kvp.Value;
             if (obj != null && obj.activeInHierarchy) { // Check if object exists and is active
                 MeshFilter mf = obj.GetComponent<MeshFilter>();
                 if (mf != null && mf.sharedMesh != null) {
                     Gizmos.DrawWireMesh(mf.sharedMesh, obj.transform.position, obj.transform.rotation, obj.transform.lossyScale);
                 }
             }
        }
         foreach (var kvp in starToBorderObjectMap) {
             GameObject obj = kvp.Value;
             if (obj != null && obj.activeInHierarchy) {
                 MeshFilter mf = obj.GetComponent<MeshFilter>();
                 if (mf != null && mf.sharedMesh != null) {
                     Gizmos.DrawWireMesh(mf.sharedMesh, obj.transform.position, obj.transform.rotation, obj.transform.lossyScale);
                 }
             }
         }

        // --- Draw Adjacency Lines between FINAL REAL sites ---
        if (realSiteAdjacency != null && realSiteAdjacency.Count > 0) {
             Gizmos.color = Color.green;
             var drawnPairs = new HashSet<(Vector2, Vector2)>(); // Avoid drawing lines twice

             foreach(var kvp in realSiteAdjacency) {
                 Vector2 siteA = kvp.Key;
                 // Draw lines slightly above the overlay
                 Vector3 p1 = new Vector3(siteA.x, yOffset + 0.1f, siteA.y);

                 foreach(var neighborSiteB in kvp.Value) {
                     // Ensure consistent pair ordering for the hash set to avoid duplicates
                     // Use Vector2 comparison logic that's consistent
                     var pair = (siteA.x < neighborSiteB.x || (siteA.x == neighborSiteB.x && siteA.y < neighborSiteB.y))
                              ? (siteA, neighborSiteB)
                              : (neighborSiteB, siteA);

                     if (drawnPairs.Add(pair)) { // .Add returns true if the pair was new
                         Vector3 p2 = new Vector3(neighborSiteB.x, yOffset + 0.1f, neighborSiteB.y);
                         Gizmos.DrawLine(p1, p2);
                     }
                 }
             }
        }
    }
#endif // UNITY_EDITOR
    #endregion
    public static class PolygonUtils
    {
        // Small tolerance for floating point comparisons if needed,
        // but Vector2 == overload often suffices in Unity.
        private const float Epsilon = 1e-5f;

        public struct FinalizeLoopResult
        {
            public List<Vector2> Vertices;
            public bool IsClosed; // Indicates if the path should be treated as closed
        }

        /// <summary>
        /// Filters a list of vertices to remove immediate backtracks (A -> B -> A pattern).
        /// It keeps the first 'A', but skips 'B' and the second 'A'.
        /// </summary>
        /// <param name="vertices">Input list of vertices.</param>
        /// <param name="enableVerboseLogging">Enable debug logging.</param>
        /// <returns>A new list with backtracks removed.</returns>
        public static List<Vector2> FilterBacktracks(List<Vector2> vertices, bool enableVerboseLogging = false)
        {
            if (vertices == null || vertices.Count < 3)
            {
                // Need at least 3 points to form a backtrack. Return original or copy.
                return vertices == null ? new List<Vector2>() : new List<Vector2>(vertices);
            }

            List<Vector2> filteredVertices = new List<Vector2>();
            int i = 0;
            while (i < vertices.Count)
            {
                // Check if the pattern P[i] -> P[i+1] -> P[i+2] exists and P[i+2] == P[i]
                // Using Vector2 == which uses Unity's kEpsilon for approximate comparison.
                if (i + 2 < vertices.Count && vertices[i] == vertices[i + 2])
                {
                    // Backtrack detected: P[i] -> P[i+1] -> P[i]
                    // Keep P[i]
                    filteredVertices.Add(vertices[i]);
                    if (enableVerboseLogging) Debug.Log($"FilterBacktracks: Detected backtrack at index {i}. Keeping {vertices[i]}, skipping {vertices[i + 1]} and {vertices[i + 2]}.");
                    // Skip P[i+1] and P[i+2] by advancing the index by 3
                    i += 3;
                }
                else
                {
                    // No backtrack starting at P[i], or near the end of the list
                    // Keep the current point P[i]
                    filteredVertices.Add(vertices[i]);
                    // Advance to the next point
                    i += 1;
                }
            }
            return filteredVertices;
        }

        /// <summary>
        /// Filters points that are too close to the *previous accepted* point in the sequence.
        /// </summary>
        /// <param name="vertices">Input list of vertices (ideally after backtrack filtering).</param>
        /// <param name="minDistance">The minimum distance required between consecutive points.</param>
        /// <param name="enableVerboseLogging">Enable debug logging.</param>
        /// <returns>A new list with close points removed.</returns>
        public static List<Vector2> FilterClosePoints(List<Vector2> vertices, float minDistance, bool enableVerboseLogging = false)
        {
            if (vertices == null || vertices.Count < 2)
            {
                // Need at least 2 points to compare distance. Return original or copy.
                return vertices == null ? new List<Vector2>() : new List<Vector2>(vertices);
            }
            // Ensure minDistance is positive
            if (minDistance <= 0) return new List<Vector2>(vertices);

            List<Vector2> filteredVertices = new List<Vector2> { vertices[0] }; // Always keep the first point

            for (int i = 1; i < vertices.Count; i++)
            {
                // Calculate distance between current point and the last point *added* to the filtered list.
                float dist = Vector2.Distance(vertices[i], filteredVertices[filteredVertices.Count - 1]);

                if (dist >= minDistance)
                {
                    // If distance is sufficient, keep the current point
                    filteredVertices.Add(vertices[i]);
                }
                else
                {
                    if (enableVerboseLogging) Debug.Log($"FilterClosePoints: Removing point {i} ({vertices[i]}) - too close ({dist:F4}) to previously kept point {filteredVertices[filteredVertices.Count - 1]}. Threshold: {minDistance}");
                }
            }
            return filteredVertices;
        }

       /// <summary>
    /// Attempts to finalize the list as a closed loop, checking for stranded points
    /// and whether the start/end points match.
    /// </summary>
    /// <returns>A struct containing the final vertex list and a boolean indicating if it's closed.</returns>
    public static FinalizeLoopResult FinalizeLoop(List<Vector2> vertices, bool enableVerboseLogging = false)
    {
        if (vertices == null || vertices.Count < 3)
        {
            if (enableVerboseLogging && vertices != null) Debug.LogWarning($"FinalizeLoop: Not enough vertices ({vertices.Count}) to reliably determine loop status. Treating as open.");
            return new FinalizeLoopResult
            {
                Vertices = vertices == null ? new List<Vector2>() : new List<Vector2>(vertices),
                IsClosed = false // Cannot be closed with < 3 points
            };
        }

        Vector2 firstPoint = vertices[0];
        Vector2 secondPoint = vertices[1];
        Vector2 lastPoint = vertices[vertices.Count - 1];

        // Check 1: Stranded point case (P[1] == P[Last] and P[0] != P[1])
        bool secondPointMatchesLast = (secondPoint == lastPoint);
        bool firstPointIsDistinct = (firstPoint != secondPoint);
        if (secondPointMatchesLast && firstPointIsDistinct)
        {
            if (enableVerboseLogging) Debug.Log($"FinalizeLoop: Detected stranded first point {firstPoint}. Path loops from {secondPoint} to {lastPoint}. Removing first point.");
            // Return vertices from index 1 onwards, considered closed.
            return new FinalizeLoopResult
            {
                Vertices = vertices.GetRange(1, vertices.Count - 1),
                IsClosed = true // Stranded point implies it was meant to be closed
            };
        }

        // Check 2: Already closed (P[0] == P[Last])
        bool firstPointMatchesLast = (firstPoint == lastPoint);
        if (firstPointMatchesLast)
        {
            if (enableVerboseLogging) Debug.Log("FinalizeLoop: Path already forms a closed loop (P[0] == P[Last]).");
            return new FinalizeLoopResult
            {
                Vertices = new List<Vector2>(vertices), // Return a copy
                IsClosed = true
            };
        }

        // Optional Check 3: End point is close to start point (suggests intended closure)
        float closeThreshold = 0.5f; // Adjust as needed
        if (Vector2.Distance(firstPoint, lastPoint) < closeThreshold)
        {
             if (enableVerboseLogging) Debug.Log($"FinalizeLoop: End point {lastPoint} is close to start point {firstPoint}. Forcing closure.");
             var closedVertices = new List<Vector2>(vertices);
             closedVertices[closedVertices.Count - 1] = firstPoint; // Snap last point to first
              return new FinalizeLoopResult
              {
                  Vertices = closedVertices,
                  IsClosed = true
              };
        }


        // If none of the above, treat as an open path
        if (enableVerboseLogging) Debug.LogWarning("FinalizeLoop: Path does not appear explicitly closed. Treating as open.");
        return new FinalizeLoopResult
        {
            Vertices = new List<Vector2>(vertices), // Return a copy
            IsClosed = false
        };
    }

    /// <summary>
    /// Smooths a polygon using Chaikin's corner-cutting algorithm.
    /// Ensures the smoothed polygon generally stays inside the original.
    /// </summary>
    /// <param name="points">Input list of vertices.</param>
    /// <param name="iterations">Number of smoothing iterations.</param>
    /// <param name="ratio">Corner cutting ratio (0.25 is standard).</param>
    /// <param name="closed">Whether the input points represent a closed loop.</param>
    /// <returns>A new list containing the smoothed vertices.</returns>
    public static List<Vector2> ChaikinSmooth(List<Vector2> points, int iterations = 1, float ratio = 0.25f, bool closed = false)
    {
        if (points == null || points.Count < 2 || iterations < 1)
        {
            // Not enough points or iterations to smooth
            return points == null ? new List<Vector2>() : new List<Vector2>(points);
        }

        List<Vector2> currentPoints = new List<Vector2>(points); // Start with a copy

        for (int iter = 0; iter < iterations; iter++)
        {
            int n = currentPoints.Count;
            if (n < 2) break; // Cannot smooth further

            List<Vector2> newPoints = new List<Vector2>();

            if (closed)
            {
                if (n < 3) break; // Need at least 3 points for a closed loop smoothing iteration
                newPoints.Capacity = n * 2; // Pre-allocate approximate size

                for (int i = 0; i < n; i++)
                {
                    Vector2 p0 = currentPoints[i];
                    Vector2 p1 = currentPoints[(i + 1) % n]; // Wrap around using modulo

                    // Calculate the two new points for the segment p0 -> p1
                    Vector2 Q = Vector2.Lerp(p0, p1, ratio);     // Point ratio*100% along the segment from p0
                    Vector2 R = Vector2.Lerp(p0, p1, 1.0f - ratio); // Point ratio*100% along the segment from p1

                    newPoints.Add(R); // Add point closer to p0 first (Using Lerp: 1-ratio gives point closer to p0)
                    newPoints.Add(Q); // Add point closer to p1 second
                }
            }
            else // Open path
            {
                newPoints.Capacity = (n - 1) * 2 + 2; // Pre-allocate approximate size
                newPoints.Add(currentPoints[0]); // Keep the original start point

                for (int i = 0; i < n - 1; i++)
                {
                    Vector2 p0 = currentPoints[i];
                    Vector2 p1 = currentPoints[i + 1];

                    // Calculate the two new points for the segment p0 -> p1
                    Vector2 Q = Vector2.Lerp(p0, p1, ratio);
                    Vector2 R = Vector2.Lerp(p0, p1, 1.0f - ratio);

                    newPoints.Add(R); // Add point closer to p0
                    newPoints.Add(Q); // Add point closer to p1
                }
                newPoints.Add(currentPoints[n - 1]); // Keep the original end point
            }

            currentPoints = newPoints; // Update points for the next iteration
        }

        return currentPoints;
    }
    }

}

