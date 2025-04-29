using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Interface for controlling a generated Voronoi overlay.
/// Allows external scripts (like GalaxyGenerator) to interact with the overlay's
/// appearance, structure, and regeneration without depending on the specific
/// implementation details (like VoronoiOverlayGenerator).
/// </summary>
public interface IVoronoiOverlayController
{
    /// <summary>
    /// Gets the StarSystemData associated with a given Voronoi site position,
    /// based on the last generated overlay.
    /// </summary>
    /// <param name="sitePosition">The 2D position (X, Z) of the Voronoi site.</param>
    /// <returns>The corresponding StarSystemData, or null if no star is associated with that site position in the last generation.</returns>
    StarSystemData GetStarSystemFromSite(Vector2 sitePosition);

    /// <summary>
    /// Gets the 2D site position (X, Z) associated with a given StarSystemData,
    /// based on the last generated overlay.
    /// </summary>
    /// <param name="star">The star system.</param>
    /// <param name="sitePosition">Outputs the 2D site position if found.</param>
    /// <returns>True if the star system was found and had a corresponding site in the last generation, false otherwise.</returns>
    bool GetSiteFromStarSystem(StarSystemData star, out Vector2 sitePosition);

    /// <summary>
    /// Enables (shows) the visual representation (fill and border) of a specific Voronoi zone
    /// corresponding to the given star system. Assumes the zone was generated previously.
    /// </summary>
    /// <param name="star">The star system whose zone should be enabled.</param>
    /// <returns>True if the zone's GameObjects were found and enabled, false otherwise (e.g., star not found, zone not generated).</returns>
    bool EnableZone(StarSystemData star);

    /// <summary>
    /// Disables (hides) the visual representation (fill and border) of a specific Voronoi zone
    /// corresponding to the given star system.
    /// </summary>
    /// <param name="star">The star system whose zone should be disabled.</param>
    /// <returns>True if the zone's GameObjects were found and disabled, false otherwise.</returns>
    bool DisableZone(StarSystemData star);

    /// <summary>
    /// Sets the primary color of a specific Voronoi zone's fill material.
    /// Relies on the implementation having a configurable primary color property name.
    /// </summary>
    /// <param name="star">The star system whose zone color should be changed.</param>
    /// <param name="color">The new color (including alpha for transparency).</param>
    /// <returns>True if the zone and its material were found and the color property was successfully set, false otherwise.</returns>
    bool SetZoneColor(StarSystemData star, Color color);

    /// <summary>
    /// Gets the current primary color of a specific Voronoi zone's fill material.
    /// </summary>
    /// <param name="star">The star system whose zone color is requested.</param>
    /// <param name="color">Outputs the current color if found.</param>
    /// <returns>True if the zone, its material, and the color property were found, false otherwise.</returns>
    bool GetZoneColor(StarSystemData star, out Color color);

    /// <summary>
    /// Sets a specific float property on a specific Voronoi zone's fill material.
    /// Allows controlling shader parameters like emission strength, metallic, smoothness etc.
    /// </summary>
    /// <param name="star">The star system whose zone material should be changed.</param>
    /// <param name="propertyName">The name of the float property in the material's shader (e.g., "_Metallic").</param>
    /// <param name="value">The new float value.</param>
    /// <returns>True if the zone, its material were found and the property was successfully set, false otherwise.</returns>
    bool SetZoneMaterialFloat(StarSystemData star, string propertyName, float value);

    /// <summary>
    /// Sets a specific color property on a specific Voronoi zone's fill material.
    /// Allows controlling secondary colors, emission colors etc.
    /// </summary>
    /// <param name="star">The star system whose zone material should be changed.</param>
    /// <param name="propertyName">The name of the color property in the material's shader (e.g., "_EmissionColor").</param>
    /// <param name="color">The new color value.</param>
    /// <returns>True if the zone, its material were found and the property was successfully set, false otherwise.</returns>
    bool SetZoneMaterialColor(StarSystemData star, string propertyName, Color color);

    /// <summary>
    /// Adds a request to merge the zone of 'starToMergeFrom' into the zone of 'starToMergeInto'.
    /// These requests are processed during the next call to ApplyQueuedMergesAndRegenerate.
    /// Queuing allows multiple merges to be defined before triggering a potentially expensive regeneration.
    /// </summary>
    /// <param name="starToMergeInto">The star system whose zone will expand.</param>
    /// <param name="starToMergeFrom">The star system whose zone will be consumed.</param>
    /// <returns>True if the request is valid (non-null, different stars) and was successfully queued, false otherwise.</returns>
    bool QueueMergeRequest(StarSystemData starToMergeInto, StarSystemData starToMergeFrom);

    /// <summary>
    /// Clears any pending merge requests that have been queued but not yet applied.
    /// </summary>
    void ClearMergeRequests();

    /// <summary>
    /// Clears the current overlay, processes all queued merge requests (determining the final set of sites),
    /// and completely regenerates the Voronoi overlay based on the provided list of star systems and the merge results.
    /// This is the primary method to generate or update the overlay after structural changes.
    /// </summary>
    /// <param name="currentStarSystems">The complete, current list of star systems to use as the basis for regeneration.</param>
    void ApplyQueuedMergesAndRegenerate(List<StarSystemData> currentStarSystems);

    /// <summary>
    /// Regenerates the overlay using the exact same set of star systems used in the *last* successful call to
    /// ApplyQueuedMergesAndRegenerate or RegenerateOverlay. Does NOT apply any newly queued merges.
    /// Useful for refreshing visuals if star positions changed slightly without altering which stars exist,
    /// or if visual parameters (like materials/colors handled outside this interface) need reapplying based on the existing structure.
    /// Requires that the implementing class stores the previously used star list.
    /// </summary>
    void RegenerateOverlay();

    /// <summary>
    /// Completely removes the generated Voronoi overlay GameObjects and cleans up associated resources (materials, meshes).
    /// Also clears internal state like merge queues and adjacency information.
    /// </summary>
    void ClearOverlay();
}