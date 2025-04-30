using UnityEngine;
using System.Collections.Generic;
using DelaunatorSharp;

public static class VoronoiGenerationHelpers
{
    private const float Epsilon = 1e-6f;
    public static List<IPoint> GenerateCircularFakePoints(Vector2 center, float radius, int numPoints)
    {
        List<IPoint> fakePoints = new List<IPoint>();
        if (radius <= 0 || numPoints <= 0)
        {
            Debug.LogWarning("GenerateCircularFakePoints: Radius or numPoints is zero or negative. Returning empty list.");
            return fakePoints;
        }
        float angleStep = 2 * Mathf.PI / numPoints;
        for (int i = 0; i < numPoints; i++)
        {
            float angle = i * angleStep;
            // Using double precision as DelaunatorSharp expects IPoint with doubles
            double x = center.x + radius * System.Math.Cos(angle);
            double y = center.y + radius * System.Math.Sin(angle);
            fakePoints.Add(new Point(x, y));
        }
        return fakePoints;
    }

    public static bool IsFakeSite(Vector2 sitePos, int realPointCount, List<Vector2> allSitesList, Dictionary<Vector2, int> sitePosToIndex, bool enableVerboseLogging)
    {
        // Prioritize checking the index mapping first, as it's more direct
        if (sitePosToIndex.TryGetValue(sitePos, out int index))
        {
            // Indices >= realPointCount belong to fake points
            return index >= realPointCount;
        }

        // Fallback check by comparing positions (less reliable due to float precision)
        // This might be needed if sitePosToIndex lookup fails for some reason
        // Only check indices in the range where fake points *should* be
        float epsilonSqr = 1e-6f * 1e-6f; // Use a local const or pass it if needed more globally
        for (int i = realPointCount; i < allSitesList.Count; i++)
        {
            // Use SqrMagnitude for efficiency
            if (Vector2.SqrMagnitude(allSitesList[i] - sitePos) < epsilonSqr)
            {
                if (enableVerboseLogging) Debug.LogWarning($"IsFakeSite: Site {sitePos} identified as fake via position comparison (index {i}). Index lookup failed.");
                return true; // Found a matching position in the fake point range
            }
        }
        if (enableVerboseLogging) Debug.LogWarning($"IsFakeSite: Site {sitePos} could not be found in sitePosToIndex map and did not match any fake point positions. Assuming not fake.");
        return false; // Not found in map or by position comparison
    }

    public static Vector2? CalculateCircumcenter(IPoint p1, IPoint p2, IPoint p3)
    {
        // Using double precision for calculations matching IPoint
        double ax = p1.X, ay = p1.Y;
        double bx = p2.X, by = p2.Y;
        double cx = p3.X, cy = p3.Y;

        // Calculate the denominator D
        double D = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));

        // Check for collinear points (D is close to zero)
        if (System.Math.Abs(D) < Epsilon * Epsilon)
        { // Use a small epsilon comparison
            // Points are collinear, circumcenter is undefined (or at infinity)
            return null;
        }

        // Calculate circumcenter coordinates using the formula
        double cX_d = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / D;
        double cY_d = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / D;

        // Check for potential NaN or Infinity results (should be rare if D check passes)
        if (double.IsNaN(cX_d) || double.IsNaN(cY_d) || double.IsInfinity(cX_d) || double.IsInfinity(cY_d))
        {
            Debug.LogWarning($"CalculateCircumcenter resulted in NaN or Infinity for points A({ax},{ay}), B({bx},{by}), C({cx},{cy}). Denominator D={D}");
            return null;
        }

        // Return the calculated circumcenter as a nullable Vector2
        return new Vector2((float)cX_d, (float)cY_d);
    }

    // Helper for looping indices in a list
    public static int GetLoopedIndex(int index, int listSize)
    {
        if (listSize <= 0) return 0; // Avoid division by zero or negative modulo
                                     // Handles positive and negative indices correctly
        return (index % listSize + listSize) % listSize;
    }

    // Calculates the signed area of a 2D polygon using the Shoelace formula
    // Positive area indicates counter-clockwise winding, negative indicates clockwise (in standard math coords)
    // Note: Unity's screen/UI coordinates might invert Y, affecting interpretation if used directly there.
    public static float CalculateSignedArea(List<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3) return 0f; // Not a polygon

        float area = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 p1 = polygon[i];
            Vector2 p2 = polygon[GetLoopedIndex(i + 1, polygon.Count)]; // Next vertex with wrap-around
            // Shoelace formula component: (x1*y2 - x2*y1)
            area += (p1.x * p2.y - p2.x * p1.y);
        }
        return area / 2.0f; // Final area
    }
}