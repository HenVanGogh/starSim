using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Utility class providing pathfinding algorithms for galactic network navigation.
/// </summary>
public static class PathfindingUtils
{
    /// <summary>
    /// Calculates the shortest path distance (number of jumps) between two nodes in a graph.
    /// Uses Dijkstra's algorithm to find the shortest path through the network.
    /// </summary>
    /// <typeparam name="T">Type of node in the graph</typeparam>
    /// <param name="startNode">The starting node.</param>
    /// <param name="endNode">The destination node.</param>
    /// <param name="graph">Dictionary mapping each node to a list of its connected nodes.</param>
    /// <param name="allNodes">Complete list of all nodes in the graph.</param>
    /// <returns>
    /// The minimum number of jumps required to travel from startNode to endNode.
    /// Returns -1 if no path exists between the nodes (disconnected graph).
    /// Returns 0 if startNode and endNode are the same node.
    /// </returns>
    public static int GetShortestDistance<T>(T startNode, T endNode, Dictionary<T, List<T>> graph, List<T> allNodes) where T : class
    {
        // Quick validation
        if (startNode == null || endNode == null)
        {
            Debug.LogError("Cannot calculate distance: One or both nodes are null.");
            return -1;
        }

        // If the same node is provided, distance is 0
        if (EqualityComparer<T>.Default.Equals(startNode, endNode))
        {
            return 0;
        }

        // Check if both nodes are in our graph
        if (!graph.ContainsKey(startNode) || !graph.ContainsKey(endNode))
        {
            Debug.LogWarning($"One or both nodes not found in the provided graph.");
            return -1;
        }

        // Set up Dijkstra's algorithm
        Dictionary<T, int> distances = new Dictionary<T, int>();
        HashSet<T> unvisited = new HashSet<T>();

        // Initialize distances: 0 for start, "infinity" for others
        foreach (var node in allNodes)
        {
            if (EqualityComparer<T>.Default.Equals(node, startNode))
                distances[node] = 0;
            else
                distances[node] = int.MaxValue;

            unvisited.Add(node);
        }

        // Process nodes until we've either found our target or exhausted all reachable nodes
        while (unvisited.Count > 0)
        {
            // Find unvisited node with smallest distance
            T current = default;
            int smallestDistance = int.MaxValue;

            foreach (var node in unvisited)
            {
                if (distances[node] < smallestDistance)
                {
                    smallestDistance = distances[node];
                    current = node;
                }
            }

            // If smallest distance is "infinity", there's no path to the remaining nodes
            if (current == null || smallestDistance == int.MaxValue)
            {
                break;
            }

            // If we've reached our destination, return the distance
            if (EqualityComparer<T>.Default.Equals(current, endNode))
            {
                return distances[endNode];
            }

            // Remove current from unvisited
            unvisited.Remove(current);

            // Check all neighbors of current node
            if (graph.TryGetValue(current, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    // Skip null neighbors
                    if (neighbor == null)
                        continue;

                    // For jumps, each edge has a "weight" of 1
                    int distanceThroughCurrent = distances[current] + 1;

                    // If we found a shorter path to this neighbor
                    if (distanceThroughCurrent < distances[neighbor])
                    {
                        distances[neighbor] = distanceThroughCurrent;
                    }
                }
            }
        }

        // If we got here without returning, there's no path to the destination
        return -1;
    }

    /// <summary>
    /// Returns the full path of nodes that forms the shortest route between two nodes.
    /// </summary>
    /// <typeparam name="T">Type of node in the graph</typeparam>
    /// <param name="startNode">The starting node.</param>
    /// <param name="endNode">The destination node.</param>
    /// <param name="graph">Dictionary mapping each node to a list of its connected nodes.</param>
    /// <param name="allNodes">Complete list of all nodes in the graph.</param>
    /// <returns>
    /// A list containing the sequence of nodes forming the shortest path, including start and end nodes.
    /// Returns an empty list if no path exists or if input parameters are invalid.
    /// </returns>
    public static List<T> GetShortestPath<T>(T startNode, T endNode, Dictionary<T, List<T>> graph, List<T> allNodes) where T : class
    {
        List<T> path = new List<T>();

        // Quick validation
        if (startNode == null || endNode == null)
        {
            Debug.LogError("Cannot find path: One or both nodes are null.");
            return path;
        }

        // If the same node is provided, the path is just that node
        if (EqualityComparer<T>.Default.Equals(startNode, endNode))
        {
            path.Add(startNode);
            return path;
        }

        // Check if both nodes are in our graph
        if (!graph.ContainsKey(startNode) || !graph.ContainsKey(endNode))
        {
            Debug.LogWarning($"One or both nodes not found in the provided graph.");
            return path;
        }

        // Set up for Dijkstra's algorithm with path tracking
        Dictionary<T, int> distances = new Dictionary<T, int>();
        Dictionary<T, T> previous = new Dictionary<T, T>();
        HashSet<T> unvisited = new HashSet<T>();

        // Initialize
        foreach (var node in allNodes)
        {
            distances[node] = EqualityComparer<T>.Default.Equals(node, startNode) ? 0 : int.MaxValue;
            previous[node] = default;
            unvisited.Add(node);
        }

        // Process nodes until we've either found our target or exhausted all reachable nodes
        while (unvisited.Count > 0)
        {
            // Find unvisited node with smallest distance
            T current = default;
            int smallestDistance = int.MaxValue;

            foreach (var node in unvisited)
            {
                if (distances[node] < smallestDistance)
                {
                    smallestDistance = distances[node];
                    current = node;
                }
            }

            // If smallest distance is "infinity", there's no path to the remaining nodes
            if (current == null || smallestDistance == int.MaxValue)
            {
                break;
            }

            // If we've reached our destination, reconstruct the path
            if (EqualityComparer<T>.Default.Equals(current, endNode))
            {
                // Start from the end and work backwards
                T pathNode = endNode;
                while (pathNode != null)
                {
                    path.Insert(0, pathNode); // Add to start of list to get correct order
                    previous.TryGetValue(pathNode, out pathNode);
                }
                return path;
            }

            unvisited.Remove(current);

            // Check all neighbors
            if (graph.TryGetValue(current, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (neighbor == null) continue;

                    int distanceThroughCurrent = distances[current] + 1;

                    if (distanceThroughCurrent < distances[neighbor])
                    {
                        distances[neighbor] = distanceThroughCurrent;
                        previous[neighbor] = current; // Track the path
                    }
                }
            }
        }

        // No path found
        return path; // Empty list
    }
}