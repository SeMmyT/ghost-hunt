using System.Collections.Generic;
using UnityEngine;

namespace GhostHunt.Maze
{
    /// <summary>
    /// Maze-aware Area of Interest for network optimization.
    /// Uses corridor-graph reachability instead of Euclidean distance.
    /// Walls = perfect AOI boundaries. Player 5m away through walls gets zero updates.
    /// This is the key architectural advantage: maze topology makes interest management
    /// geometrically exact, not approximate.
    /// </summary>
    public class MazeAOI
    {
        private int[,] _grid;
        private int _width;
        private int _height;
        private float _cellSize;

        // Precomputed reachability graph: for each corridor cell, which other cells
        // are reachable within N steps (without going through walls)
        private Dictionary<(int, int), HashSet<(int, int)>> _reachabilityCache;
        private int _maxReachSteps = 8; // AOI radius in grid steps

        public MazeAOI(int[,] grid, int width, int height, float cellSize = 2f)
        {
            _grid = grid;
            _width = width;
            _height = height;
            _cellSize = cellSize;
            _reachabilityCache = new Dictionary<(int, int), HashSet<(int, int)>>();

            PrecomputeReachability();
        }

        /// <summary>
        /// Check if position B is within AOI of position A.
        /// Uses maze corridor graph, not straight-line distance.
        /// </summary>
        public bool IsInAOI(Vector3 posA, Vector3 posB)
        {
            var cellA = WorldToGrid(posA);
            var cellB = WorldToGrid(posB);

            if (!_reachabilityCache.TryGetValue(cellA, out var reachable))
                return false;

            return reachable.Contains(cellB);
        }

        /// <summary>
        /// Get all players within AOI of a given position.
        /// Returns grid cells that are reachable via corridors.
        /// </summary>
        public HashSet<(int, int)> GetReachableCells(Vector3 worldPos)
        {
            var cell = WorldToGrid(worldPos);
            if (_reachabilityCache.TryGetValue(cell, out var reachable))
                return reachable;
            return new HashSet<(int, int)>();
        }

        private void PrecomputeReachability()
        {
            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    if (_grid[x, y] == MazeGenerator.Wall) continue;

                    var reachable = new HashSet<(int, int)>();
                    BFS(x, y, _maxReachSteps, reachable);
                    _reachabilityCache[(x, y)] = reachable;
                }
            }
        }

        private void BFS(int startX, int startY, int maxSteps, HashSet<(int, int)> result)
        {
            var queue = new Queue<(int x, int y, int steps)>();
            var visited = new HashSet<(int, int)>();

            queue.Enqueue((startX, startY, 0));
            visited.Add((startX, startY));

            while (queue.Count > 0)
            {
                var (cx, cy, steps) = queue.Dequeue();
                result.Add((cx, cy));

                if (steps >= maxSteps) continue;

                // 4-directional neighbors (no diagonals in Pac-Man mazes)
                TryEnqueue(cx - 1, cy, steps + 1, queue, visited);
                TryEnqueue(cx + 1, cy, steps + 1, queue, visited);
                TryEnqueue(cx, cy - 1, steps + 1, queue, visited);
                TryEnqueue(cx, cy + 1, steps + 1, queue, visited);
            }
        }

        private void TryEnqueue(int x, int y, int steps,
            Queue<(int x, int y, int steps)> queue, HashSet<(int, int)> visited)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return;
            if (_grid[x, y] == MazeGenerator.Wall) return;
            if (visited.Contains((x, y))) return;

            visited.Add((x, y));
            queue.Enqueue((x, y, steps));
        }

        private (int, int) WorldToGrid(Vector3 worldPos)
        {
            int x = Mathf.RoundToInt(worldPos.x / _cellSize + _width / 2f);
            int y = Mathf.RoundToInt(worldPos.z / _cellSize + _height / 2f);
            x = Mathf.Clamp(x, 0, _width - 1);
            y = Mathf.Clamp(y, 0, _height - 1);
            return (x, y);
        }
    }
}
