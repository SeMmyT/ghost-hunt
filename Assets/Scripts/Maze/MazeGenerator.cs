using System.Collections.Generic;
using UnityEngine;
using GhostHunt.Core;

namespace GhostHunt.Maze
{
    /// <summary>
    /// Procedural maze generator using symmetric DFS spanning tree + loop addition.
    /// Guarantees: fully connected, no dead ends, left-right symmetry with
    /// controlled breaks, portals at edges, ghost spawns at center.
    ///
    /// Algorithm:
    /// 1. Randomized DFS on left-half coarse grid → connected spanning tree
    /// 2. Mirror to right half
    /// 3. Open ~35% of remaining walls → loops (eliminates dead ends)
    /// 4. Connect halves through center column
    /// 5. Iterative dead-end cleanup
    /// 6. Place spawns, portals, collectibles
    /// 7. Flood-fill connectivity validation
    ///
    /// Dimensions are forced to odd for clean coarse-grid alignment.
    /// Coarse nodes sit at odd coordinates (2i+1, 2j+1), walls at even.
    /// </summary>
    public class MazeGenerator
    {
        public int Width { get; }
        public int Height { get; }

        public const int Wall = 0;
        public const int Corridor = 1;
        public const int Intersection = 2;
        public const int GhostSpawn = 3;
        public const int TargetSpawn = 4;
        public const int PowerPellet = 5;
        public const int Portal = 6;
        public const int Collectible = 7;

        private int[,] _grid;
        private System.Random _rng;

        public MazeGenerator(int width = 29, int height = 31, int seed = 0)
        {
            Width = width % 2 == 0 ? width + 1 : width;
            Height = height % 2 == 0 ? height + 1 : height;
            _rng = seed != 0 ? new System.Random(seed) : new System.Random();
        }

        public int[,] Generate()
        {
            _grid = new int[Width, Height];

            CarveSpanningTree();
            AddLoops(0.35f);
            ConnectHalves();
            PlaceSpawns();
            PlacePortals();
            RemoveDeadEnds();
            ClassifyIntersections();
            PlaceCollectibles();

            if (!ValidateConnectivity())
                Debug.LogError("[MazeGen] Maze is not fully connected!");

            return _grid;
        }

        /// <summary>
        /// Randomized DFS on left-half coarse grid, mirrored to right.
        /// Coarse node (c,r) → grid position (2c+1, 2r+1).
        /// Wall between adjacent nodes carved to connect them.
        /// </summary>
        private void CarveSpanningTree()
        {
            int centerX = Width / 2;
            int halfCols = centerX / 2;
            int rows = (Height - 1) / 2;

            var visited = new bool[halfCols, rows];
            var stack = new Stack<(int c, int r)>();

            visited[0, 0] = true;
            SetSymmetric(1, 1, Corridor);
            stack.Push((0, 0));

            while (stack.Count > 0)
            {
                var (c, r) = stack.Peek();
                var neighbors = new List<(int c, int r)>();

                if (c > 0 && !visited[c - 1, r]) neighbors.Add((c - 1, r));
                if (c + 1 < halfCols && !visited[c + 1, r]) neighbors.Add((c + 1, r));
                if (r > 0 && !visited[c, r - 1]) neighbors.Add((c, r - 1));
                if (r + 1 < rows && !visited[c, r + 1]) neighbors.Add((c, r + 1));

                if (neighbors.Count == 0)
                {
                    stack.Pop();
                    continue;
                }

                var (nc, nr) = neighbors[_rng.Next(neighbors.Count)];
                visited[nc, nr] = true;

                // Carve destination node + wall between current and neighbor
                SetSymmetric(nc * 2 + 1, nr * 2 + 1, Corridor);
                SetSymmetric(c + nc + 1, r + nr + 1, Corridor);

                stack.Push((nc, nr));
            }
        }

        /// <summary>
        /// Open additional walls between coarse nodes to create loops.
        /// Operates on left half, mirrored to right.
        /// </summary>
        private void AddLoops(float probability)
        {
            int centerX = Width / 2;
            int halfCols = centerX / 2;
            int rows = (Height - 1) / 2;

            for (int c = 0; c < halfCols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    // Horizontal wall to right neighbor
                    if (c + 1 < halfCols && _rng.NextDouble() < probability)
                    {
                        int wx = c * 2 + 2, wy = r * 2 + 1;
                        if (_grid[wx, wy] == Wall)
                            SetSymmetric(wx, wy, Corridor);
                    }

                    // Vertical wall to bottom neighbor
                    if (r + 1 < rows && _rng.NextDouble() < probability)
                    {
                        int wx = c * 2 + 1, wy = r * 2 + 2;
                        if (_grid[wx, wy] == Wall)
                            SetSymmetric(wx, wy, Corridor);
                    }
                }
            }
        }

        /// <summary>
        /// Open center column at several y positions to link left and right halves.
        /// Also bridges any wall gap between center and adjacent coarse nodes.
        /// Guarantees at least 3 connections.
        /// </summary>
        private void ConnectHalves()
        {
            int cx = Width / 2;
            int rows = (Height - 1) / 2;
            int connections = 0;

            for (int r = 0; r < rows; r++)
            {
                if (_rng.NextDouble() < 0.45)
                {
                    OpenCenter(cx, r * 2 + 1);
                    connections++;
                }
            }

            int attempts = 0;
            while (connections < 3 && attempts < 100)
            {
                int r = _rng.Next(rows);
                int y = r * 2 + 1;
                if (_grid[cx, y] == Wall)
                {
                    OpenCenter(cx, y);
                    connections++;
                }
                attempts++;
            }
        }

        private void OpenCenter(int cx, int y)
        {
            _grid[cx, y] = Corridor;
            // Bridge gap between center and adjacent coarse columns
            // (needed when center-to-coarse distance > 1, e.g. width=31)
            if (cx > 0 && _grid[cx - 1, y] == Wall)
                _grid[cx - 1, y] = Corridor;
            if (cx < Width - 1 && _grid[cx + 1, y] == Wall)
                _grid[cx + 1, y] = Corridor;
        }

        /// <summary>
        /// Iteratively open walls adjacent to dead ends until none remain.
        /// This is where controlled symmetry breaks occur.
        /// </summary>
        private void RemoveDeadEnds()
        {
            bool changed = true;
            int passes = 0;

            while (changed && passes < 100)
            {
                changed = false;
                passes++;

                for (int x = 1; x < Width - 1; x++)
                {
                    for (int y = 1; y < Height - 1; y++)
                    {
                        if (_grid[x, y] == Wall || _grid[x, y] == GhostSpawn)
                            continue;

                        if (CountOpenNeighbors(x, y) > 1) continue;

                        var walls = new List<(int x, int y)>();
                        if (x > 1 && _grid[x - 1, y] == Wall) walls.Add((x - 1, y));
                        if (x < Width - 2 && _grid[x + 1, y] == Wall) walls.Add((x + 1, y));
                        if (y > 1 && _grid[x, y - 1] == Wall) walls.Add((x, y - 1));
                        if (y < Height - 2 && _grid[x, y + 1] == Wall) walls.Add((x, y + 1));

                        if (walls.Count > 0)
                        {
                            var (wx, wy) = walls[_rng.Next(walls.Count)];
                            _grid[wx, wy] = Corridor;
                            changed = true;
                        }
                    }
                }
            }

            if (passes >= 100)
                Debug.LogWarning("[MazeGen] Dead end removal hit iteration limit");
        }

        /// <summary>
        /// Mark corridor cells with 3+ open neighbors as Intersection.
        /// Used by ghost AI for decision points.
        /// </summary>
        private void ClassifyIntersections()
        {
            for (int x = 1; x < Width - 1; x++)
                for (int y = 1; y < Height - 1; y++)
                    if (_grid[x, y] == Corridor && CountOpenNeighbors(x, y) >= 3)
                        _grid[x, y] = Intersection;
        }

        private void PlaceSpawns()
        {
            int cx = Width / 2;
            int cy = Height / 2;

            // Ghost spawns: center cluster (up to 4 ghosts)
            _grid[cx, cy] = GhostSpawn;
            if (cx > 0) _grid[cx - 1, cy] = GhostSpawn;
            if (cx < Width - 1) _grid[cx + 1, cy] = GhostSpawn;
            if (cy > 0) _grid[cx, cy - 1] = GhostSpawn;

            // Target spawn: bottom center, ensure connected to maze
            int ty = Height - 2;
            _grid[cx, ty] = TargetSpawn;
            EnsureCorridor(cx - 1, ty);
            EnsureCorridor(cx + 1, ty);
            EnsureCorridor(cx, ty - 1);
        }

        private void PlacePortals()
        {
            int midY = Height / 2;
            _grid[0, midY] = Portal;
            _grid[Width - 1, midY] = Portal;
            EnsureCorridor(1, midY);
            EnsureCorridor(Width - 2, midY);
        }

        private void PlaceCollectibles()
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (_grid[x, y] == Corridor || _grid[x, y] == Intersection)
                        _grid[x, y] = Collectible;

            // Corners at odd coordinates (coarse nodes, guaranteed carved)
            PlacePowerPellet(1, 1);
            PlacePowerPellet(Width - 2, 1);
            PlacePowerPellet(1, Height - 2);
            PlacePowerPellet(Width - 2, Height - 2);
        }

        private void PlacePowerPellet(int x, int y)
        {
            if (InBounds(x, y) && _grid[x, y] != Wall)
                _grid[x, y] = PowerPellet;
        }

        /// <summary>
        /// Flood fill from first non-wall cell. Returns true if all non-wall cells reached.
        /// </summary>
        private bool ValidateConnectivity()
        {
            int startX = -1, startY = -1;
            int totalOpen = 0;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (_grid[x, y] == Wall) continue;
                    totalOpen++;
                    if (startX == -1) { startX = x; startY = y; }
                }
            }

            if (startX == -1) return false;

            var visited = new bool[Width, Height];
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));
            visited[startX, startY] = true;
            int reached = 0;

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                reached++;

                TryEnqueueFlood(x - 1, y, visited, queue);
                TryEnqueueFlood(x + 1, y, visited, queue);
                TryEnqueueFlood(x, y - 1, visited, queue);
                TryEnqueueFlood(x, y + 1, visited, queue);
            }

            if (reached < totalOpen)
                Debug.LogWarning($"[MazeGen] {totalOpen - reached}/{totalOpen} cells unreachable");

            return reached == totalOpen;
        }

        private void TryEnqueueFlood(int x, int y, bool[,] visited,
            Queue<(int x, int y)> queue)
        {
            if (!InBounds(x, y) || visited[x, y] || _grid[x, y] == Wall) return;
            visited[x, y] = true;
            queue.Enqueue((x, y));
        }

        // --- Helpers ---

        private void SetSymmetric(int x, int y, int value)
        {
            if (InBounds(x, y)) _grid[x, y] = value;
            int mx = Width - 1 - x;
            if (InBounds(mx, y)) _grid[mx, y] = value;
        }

        private void EnsureCorridor(int x, int y)
        {
            if (InBounds(x, y) && _grid[x, y] == Wall)
                _grid[x, y] = Corridor;
        }

        private bool InBounds(int x, int y)
            => x >= 0 && x < Width && y >= 0 && y < Height;

        private int CountOpenNeighbors(int x, int y)
        {
            int n = 0;
            if (x > 0 && _grid[x - 1, y] != Wall) n++;
            if (x < Width - 1 && _grid[x + 1, y] != Wall) n++;
            if (y > 0 && _grid[x, y - 1] != Wall) n++;
            if (y < Height - 1 && _grid[x, y + 1] != Wall) n++;
            return n;
        }

        public Vector3 GridToWorld(int x, int y, float cellSize = 2f)
        {
            return new Vector3(
                (x - Width / 2f) * cellSize,
                0,
                (y - Height / 2f) * cellSize
            );
        }
    }
}
