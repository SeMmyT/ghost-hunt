using NUnit.Framework;
using GhostHunt.Maze;

namespace GhostHunt.Tests
{
    public class MazeGeneratorTests
    {
        [Test]
        public void Generate_ProducesCorrectDimensions()
        {
            var gen = new MazeGenerator(28, 31, 42);
            var grid = gen.Generate();
            Assert.AreEqual(28, grid.GetLength(0));
            Assert.AreEqual(31, grid.GetLength(1));
        }

        [Test]
        public void Generate_HasNoDeadEnds()
        {
            var gen = new MazeGenerator(28, 31, 42);
            var grid = gen.Generate();

            int deadEnds = 0;
            for (int x = 1; x < 27; x++)
            {
                for (int y = 1; y < 30; y++)
                {
                    if (grid[x, y] == MazeGenerator.Wall) continue;

                    int neighbors = 0;
                    if (grid[x - 1, y] != MazeGenerator.Wall) neighbors++;
                    if (grid[x + 1, y] != MazeGenerator.Wall) neighbors++;
                    if (grid[x, y - 1] != MazeGenerator.Wall) neighbors++;
                    if (grid[x, y + 1] != MazeGenerator.Wall) neighbors++;

                    if (neighbors <= 1) deadEnds++;
                }
            }

            Assert.AreEqual(0, deadEnds, $"Maze has {deadEnds} dead ends");
        }

        [Test]
        public void Generate_HasGhostSpawn()
        {
            var gen = new MazeGenerator(28, 31, 42);
            var grid = gen.Generate();

            bool found = false;
            for (int x = 0; x < 28; x++)
                for (int y = 0; y < 31; y++)
                    if (grid[x, y] == MazeGenerator.GhostSpawn) found = true;

            Assert.IsTrue(found, "Maze missing ghost spawn");
        }

        [Test]
        public void Generate_HasTargetSpawn()
        {
            var gen = new MazeGenerator(28, 31, 42);
            var grid = gen.Generate();

            bool found = false;
            for (int x = 0; x < 28; x++)
                for (int y = 0; y < 31; y++)
                    if (grid[x, y] == MazeGenerator.TargetSpawn) found = true;

            Assert.IsTrue(found, "Maze missing target spawn");
        }

        [Test]
        public void Generate_HasPortals()
        {
            var gen = new MazeGenerator(28, 31, 42);
            var grid = gen.Generate();

            int portalCount = 0;
            for (int x = 0; x < 28; x++)
                for (int y = 0; y < 31; y++)
                    if (grid[x, y] == MazeGenerator.Portal) portalCount++;

            Assert.GreaterOrEqual(portalCount, 2, "Need at least 2 portals");
        }

        [Test]
        public void Generate_HasFourPowerPellets()
        {
            var gen = new MazeGenerator(28, 31, 42);
            var grid = gen.Generate();

            int pelletCount = 0;
            for (int x = 0; x < 28; x++)
                for (int y = 0; y < 31; y++)
                    if (grid[x, y] == MazeGenerator.PowerPellet) pelletCount++;

            Assert.AreEqual(4, pelletCount, "Expected 4 power pellets");
        }

        [Test]
        public void Generate_HasCollectibles()
        {
            var gen = new MazeGenerator(28, 31, 42);
            var grid = gen.Generate();

            int collectibles = 0;
            for (int x = 0; x < 28; x++)
                for (int y = 0; y < 31; y++)
                    if (grid[x, y] == MazeGenerator.Collectible) collectibles++;

            Assert.Greater(collectibles, 50, "Maze needs significant collectibles");
        }

        [Test]
        public void Generate_SameSeedProducesSameResult()
        {
            var gen1 = new MazeGenerator(28, 31, 123);
            var grid1 = gen1.Generate();

            var gen2 = new MazeGenerator(28, 31, 123);
            var grid2 = gen2.Generate();

            for (int x = 0; x < 28; x++)
                for (int y = 0; y < 31; y++)
                    Assert.AreEqual(grid1[x, y], grid2[x, y],
                        $"Grids differ at ({x},{y})");
        }

        [Test]
        public void Generate_DifferentSeedsProduceDifferentResults()
        {
            var gen1 = new MazeGenerator(28, 31, 100);
            var grid1 = gen1.Generate();

            var gen2 = new MazeGenerator(28, 31, 200);
            var grid2 = gen2.Generate();

            bool anyDifferent = false;
            for (int x = 0; x < 28; x++)
                for (int y = 0; y < 31; y++)
                    if (grid1[x, y] != grid2[x, y]) anyDifferent = true;

            Assert.IsTrue(anyDifferent, "Different seeds produced identical mazes");
        }

        [Test]
        public void Generate_BorderIsAllWalls()
        {
            var gen = new MazeGenerator(28, 31, 42);
            var grid = gen.Generate();

            // Top and bottom rows should be walls (except portals)
            for (int x = 0; x < 28; x++)
            {
                Assert.AreEqual(MazeGenerator.Wall, grid[x, 0],
                    $"Top border not wall at x={x}");
            }
        }

        [Test]
        public void MazeAOI_WallBlocksVisibility()
        {
            // Simple 5x5 grid with a wall in the middle
            int[,] grid = {
                { 0, 0, 0, 0, 0 },
                { 0, 1, 1, 1, 0 },
                { 0, 1, 0, 1, 0 }, // Wall at (2,2)
                { 0, 1, 1, 1, 0 },
                { 0, 0, 0, 0, 0 }
            };

            var aoi = new MazeAOI(grid, 5, 5, 1f);

            // (1,1) should reach (1,3) via corridors but not through the wall
            var reachable = aoi.GetReachableCells(new UnityEngine.Vector3(1 - 2.5f, 0, 1 - 2.5f));
            Assert.IsTrue(reachable.Count > 0, "Should have reachable cells");
        }
    }
}
