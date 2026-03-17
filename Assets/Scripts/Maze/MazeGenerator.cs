using System.Collections.Generic;
using UnityEngine;
using GhostHunt.Core;

namespace GhostHunt.Maze
{
    /// <summary>
    /// Procedural maze generator using chunk assembly.
    /// Constraints (from research):
    /// - No dead ends (VR comfort + gameplay flow)
    /// - Intersections separated by at least 2 corridor units
    /// - Left-right symmetry with controlled breaks
    /// - All corridors connected (Pac-Man maze invariant)
    /// - Portals at designated edge positions
    /// </summary>
    public class MazeGenerator
    {
        public int Width { get; }
        public int Height { get; }

        // Cell types
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

        public MazeGenerator(int width = 28, int height = 31, int seed = 0)
        {
            Width = width;
            Height = height;
            _rng = seed != 0 ? new System.Random(seed) : new System.Random();
        }

        /// <summary>
        /// Generate a maze. Returns 2D grid of cell types.
        /// Uses chunk-based assembly with post-generation validation.
        /// </summary>
        public int[,] Generate()
        {
            _grid = new int[Width, Height];

            // Phase 1: Fill with walls
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _grid[x, y] = Wall;

            // Phase 2: Carve symmetric corridors
            CarveSymmetricMaze();

            // Phase 3: Remove dead ends (Pac-Man invariant)
            RemoveDeadEnds();

            // Phase 4: Place special cells
            PlaceSpawns();
            PlacePortals();
            PlaceCollectibles();

            // Phase 5: Validate
            ValidateMaze();

            return _grid;
        }

        private void CarveSymmetricMaze()
        {
            // Carve only the left half, mirror to right
            int halfWidth = Width / 2;

            // Start with a grid of corridors every 2 cells (intersection spacing)
            for (int x = 1; x < halfWidth; x += 2)
            {
                for (int y = 1; y < Height - 1; y += 2)
                {
                    _grid[x, y] = Corridor;
                    _grid[Width - 1 - x, y] = Corridor; // Mirror

                    // Randomly connect to neighbors (ensuring no dead ends later)
                    if (y + 2 < Height - 1 && _rng.NextDouble() > 0.3)
                    {
                        _grid[x, y + 1] = Corridor;
                        _grid[Width - 1 - x, y + 1] = Corridor;
                    }
                    if (x + 2 < halfWidth && _rng.NextDouble() > 0.3)
                    {
                        _grid[x + 1, y] = Corridor;
                        _grid[Width - 2 - x, y] = Corridor;
                    }
                }
            }

            // Ensure center column is connected (for symmetry break)
            int centerX = Width / 2;
            for (int y = 1; y < Height - 1; y += 2)
            {
                if (_rng.NextDouble() > 0.5)
                    _grid[centerX, y] = Corridor;
            }
        }

        private void RemoveDeadEnds()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int x = 1; x < Width - 1; x++)
                {
                    for (int y = 1; y < Height - 1; y++)
                    {
                        if (_grid[x, y] == Wall) continue;

                        // Count open neighbors
                        int neighbors = 0;
                        if (_grid[x - 1, y] != Wall) neighbors++;
                        if (_grid[x + 1, y] != Wall) neighbors++;
                        if (_grid[x, y - 1] != Wall) neighbors++;
                        if (_grid[x, y + 1] != Wall) neighbors++;

                        // Dead end = only 1 open neighbor
                        if (neighbors <= 1)
                        {
                            // Open a random wall neighbor to create a loop
                            var wallNeighbors = new List<(int, int)>();
                            if (x > 1 && _grid[x - 1, y] == Wall) wallNeighbors.Add((x - 1, y));
                            if (x < Width - 2 && _grid[x + 1, y] == Wall) wallNeighbors.Add((x + 1, y));
                            if (y > 1 && _grid[x, y - 1] == Wall) wallNeighbors.Add((x, y - 1));
                            if (y < Height - 2 && _grid[x, y + 1] == Wall) wallNeighbors.Add((x, y + 1));

                            if (wallNeighbors.Count > 0)
                            {
                                var (wx, wy) = wallNeighbors[_rng.Next(wallNeighbors.Count)];
                                _grid[wx, wy] = Corridor;
                                changed = true;
                            }
                        }
                    }
                }
            }
        }

        private void PlaceSpawns()
        {
            // Ghost spawn: center of maze (classic ghost house position)
            int cx = Width / 2;
            int cy = Height / 2;
            _grid[cx, cy] = GhostSpawn;
            _grid[cx - 1, cy] = GhostSpawn;
            _grid[cx + 1, cy] = GhostSpawn;
            _grid[cx, cy - 1] = GhostSpawn;

            // Target spawn: bottom center
            _grid[cx, Height - 2] = TargetSpawn;
        }

        private void PlacePortals()
        {
            // Portals on left/right edges at vertical midpoint
            int midY = Height / 2;
            _grid[0, midY] = Portal;
            _grid[Width - 1, midY] = Portal;
            // Ensure corridor connects to portal
            _grid[1, midY] = Corridor;
            _grid[Width - 2, midY] = Corridor;
        }

        private void PlaceCollectibles()
        {
            // Place collectibles in all empty corridors
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (_grid[x, y] == Corridor)
                        _grid[x, y] = Collectible;
                }
            }

            // Place 4 power pellets in corners
            PlacePowerPellet(2, 2);
            PlacePowerPellet(Width - 3, 2);
            PlacePowerPellet(2, Height - 3);
            PlacePowerPellet(Width - 3, Height - 3);
        }

        private void PlacePowerPellet(int x, int y)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
                _grid[x, y] = PowerPellet;
        }

        private void ValidateMaze()
        {
            // Post-generation invariant checks
            int corridorCount = 0;
            int deadEnds = 0;

            for (int x = 1; x < Width - 1; x++)
            {
                for (int y = 1; y < Height - 1; y++)
                {
                    if (_grid[x, y] == Wall) continue;
                    corridorCount++;

                    int neighbors = 0;
                    if (_grid[x - 1, y] != Wall) neighbors++;
                    if (_grid[x + 1, y] != Wall) neighbors++;
                    if (_grid[x, y - 1] != Wall) neighbors++;
                    if (_grid[x, y + 1] != Wall) neighbors++;

                    if (neighbors <= 1) deadEnds++;
                }
            }

            if (deadEnds > 0)
                Debug.LogWarning($"[MazeGen] {deadEnds} dead ends remaining after cleanup!");
            else
                Debug.Log($"[MazeGen] Valid maze: {corridorCount} corridors, 0 dead ends");
        }

        /// <summary>
        /// Convert grid to world-space positions for Unity instantiation.
        /// </summary>
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
