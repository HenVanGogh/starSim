using UnityEngine;
using System.Collections.Generic;
using System.Linq;


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