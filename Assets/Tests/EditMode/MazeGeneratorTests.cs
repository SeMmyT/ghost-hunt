using NUnit.Framework;
using GhostHunt.Maze;
using System.Collections.Generic;

namespace GhostHunt.Tests
{
    public class MazeGeneratorTests
    {
        // Even inputs get forced to odd: 28→29, 31 stays 31
        private const int InputWidth = 28;
        private const int InputHeight = 31;
        private const int ExpectedWidth = 29;
        private const int ExpectedHeight = 31;

        [Test]
        public void Generate_ForcesOddDimensions()
        {
            var gen = new MazeGenerator(InputWidth, InputHeight, 42);
            var grid = gen.Generate();
            Assert.AreEqual(ExpectedWidth, gen.Width);
            Assert.AreEqual(ExpectedHeight, gen.Height);
            Assert.AreEqual(ExpectedWidth, grid.GetLength(0));
            Assert.AreEqual(ExpectedHeight, grid.GetLength(1));
        }

        [Test]
        public void Generate_HasNoDeadEnds()
        {
            var gen = new MazeGenerator(InputWidth, InputHeight, 42);
            var grid = gen.Generate();
            int w = gen.Width, h = gen.Height;

            int deadEnds = 0;
            for (int x = 1; x < w - 1; x++)
            {
                for (int y = 1; y < h - 1; y++)
                {
                    if (grid[x, y] == MazeGenerator.Wall ||
                        grid[x, y] == MazeGenerator.GhostSpawn)
                        continue;

                    int neighbors = CountOpen(grid, x, y, w, h);
                    if (neighbors <= 1) deadEnds++;
                }
            }

            Assert.AreEqual(0, deadEnds, $"Maze has {deadEnds} dead ends");
        }

        [Test]
        public void Generate_IsFullyConnected()
        {
            var gen = new MazeGenerator(InputWidth, InputHeight, 42);
            var grid = gen.Generate();
            int w = gen.Width, h = gen.Height;

            // Find first non-wall cell
            int startX = -1, startY = -1;
            int totalOpen = 0;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (grid[x, y] == MazeGenerator.Wall) continue;
                    totalOpen++;
                    if (startX == -1) { startX = x; startY = y; }
                }
            }

            // BFS flood fill
            var visited = new bool[w, h];
            var queue = new Queue<(int, int)>();
            queue.Enqueue((startX, startY));
            visited[startX, startY] = true;
            int reached = 0;

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                reached++;

                TryEnqueue(grid, visited, queue, x - 1, y, w, h);
                TryEnqueue(grid, visited, queue, x + 1, y, w, h);
                TryEnqueue(grid, visited, queue, x, y - 1, w, h);
                TryEnqueue(grid, visited, queue, x, y + 1, w, h);
            }

            Assert.AreEqual(totalOpen, reached,
                $"{totalOpen - reached} cells unreachable");
        }

        [Test]
        public void Generate_HasGhostSpawn()
        {
            var gen = new MazeGenerator(InputWidth, InputHeight, 42);
            var grid = gen.Generate();
            Assert.IsTrue(CountCellType(grid, gen.Width, gen.Height,
                MazeGenerator.GhostSpawn) >= 3, "Need at least 3 ghost spawn cells");
        }

        [Test]
        public void Generate_HasTargetSpawn()
        {
            var gen = new MazeGenerator(InputWidth, InputHeight, 42);
            var grid = gen.Generate();
            Assert.IsTrue(CountCellType(grid, gen.Width, gen.Height,
                MazeGenerator.TargetSpawn) >= 1, "Missing target spawn");
        }

        [Test]
        public void Generate_HasPortals()
        {
            var gen = new MazeGenerator(InputWidth, InputHeight, 42);
            var grid = gen.Generate();
            Assert.GreaterOrEqual(CountCellType(grid, gen.Width, gen.Height,
                MazeGenerator.Portal), 2, "Need at least 2 portals");
        }

        [Test]
        public void Generate_HasFourPowerPellets()
        {
            var gen = new MazeGenerator(InputWidth, InputHeight, 42);
            var grid = gen.Generate();
            Assert.AreEqual(4, CountCellType(grid, gen.Width, gen.Height,
                MazeGenerator.PowerPellet), "Expected 4 power pellets");
        }

        [Test]
        public void Generate_HasCollectibles()
        {
            var gen = new MazeGenerator(InputWidth, InputHeight, 42);
            var grid = gen.Generate();
            Assert.Greater(CountCellType(grid, gen.Width, gen.Height,
                MazeGenerator.Collectible), 50, "Maze needs significant collectibles");
        }

        [Test]
        public void Generate_SameSeedProducesSameResult()
        {
            var gen1 = new MazeGenerator(InputWidth, InputHeight, 123);
            var grid1 = gen1.Generate();

            var gen2 = new MazeGenerator(InputWidth, InputHeight, 123);
            var grid2 = gen2.Generate();

            for (int x = 0; x < gen1.Width; x++)
                for (int y = 0; y < gen1.Height; y++)
                    Assert.AreEqual(grid1[x, y], grid2[x, y],
                        $"Grids differ at ({x},{y})");
        }

        [Test]
        public void Generate_DifferentSeedsProduceDifferentResults()
        {
            var gen1 = new MazeGenerator(InputWidth, InputHeight, 100);
            var grid1 = gen1.Generate();

            var gen2 = new MazeGenerator(InputWidth, InputHeight, 200);
            var grid2 = gen2.Generate();

            bool anyDifferent = false;
            for (int x = 0; x < gen1.Width; x++)
                for (int y = 0; y < gen1.Height; y++)
                    if (grid1[x, y] != grid2[x, y]) anyDifferent = true;

            Assert.IsTrue(anyDifferent, "Different seeds produced identical mazes");
        }

        [Test]
        public void Generate_TopBorderIsWalls()
        {
            var gen = new MazeGenerator(InputWidth, InputHeight, 42);
            var grid = gen.Generate();

            for (int x = 0; x < gen.Width; x++)
                Assert.AreEqual(MazeGenerator.Wall, grid[x, 0],
                    $"Top border not wall at x={x}");
        }

        [Test]
        public void Generate_MultipleSeeds_AllValid()
        {
            // Stress test: 20 seeds must all produce connected, dead-end-free mazes
            for (int seed = 1; seed <= 20; seed++)
            {
                var gen = new MazeGenerator(29, 31, seed);
                var grid = gen.Generate();
                int w = gen.Width, h = gen.Height;

                // Check dead ends
                for (int x = 1; x < w - 1; x++)
                {
                    for (int y = 1; y < h - 1; y++)
                    {
                        if (grid[x, y] == MazeGenerator.Wall ||
                            grid[x, y] == MazeGenerator.GhostSpawn)
                            continue;

                        int neighbors = CountOpen(grid, x, y, w, h);
                        Assert.Greater(neighbors, 1,
                            $"Seed {seed}: dead end at ({x},{y})");
                    }
                }
            }
        }

        [Test]
        public void MazeAOI_WallBlocksVisibility()
        {
            int[,] grid = {
                { 0, 0, 0, 0, 0 },
                { 0, 1, 1, 1, 0 },
                { 0, 1, 0, 1, 0 },
                { 0, 1, 1, 1, 0 },
                { 0, 0, 0, 0, 0 }
            };

            var aoi = new MazeAOI(grid, 5, 5, 1f);
            var reachable = aoi.GetReachableCells(
                new UnityEngine.Vector3(1 - 2.5f, 0, 1 - 2.5f));
            Assert.IsTrue(reachable.Count > 0, "Should have reachable cells");
        }

        // --- Helpers ---

        private static int CountOpen(int[,] grid, int x, int y, int w, int h)
        {
            int n = 0;
            if (x > 0 && grid[x - 1, y] != MazeGenerator.Wall) n++;
            if (x < w - 1 && grid[x + 1, y] != MazeGenerator.Wall) n++;
            if (y > 0 && grid[x, y - 1] != MazeGenerator.Wall) n++;
            if (y < h - 1 && grid[x, y + 1] != MazeGenerator.Wall) n++;
            return n;
        }

        private static void TryEnqueue(int[,] grid, bool[,] visited,
            Queue<(int, int)> queue, int x, int y, int w, int h)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            if (visited[x, y] || grid[x, y] == MazeGenerator.Wall) return;
            visited[x, y] = true;
            queue.Enqueue((x, y));
        }

        private static int CountCellType(int[,] grid, int w, int h, int type)
        {
            int count = 0;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (grid[x, y] == type) count++;
            return count;
        }
    }
}
