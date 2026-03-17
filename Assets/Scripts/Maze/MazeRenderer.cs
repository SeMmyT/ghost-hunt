using UnityEngine;
using GhostHunt.Core;

namespace GhostHunt.Maze
{
    /// <summary>
    /// Converts MazeGenerator grid data into Unity GameObjects.
    /// Instantiates walls, corridors, collectibles, portals, power pellets.
    /// Uses simple cube primitives — the dither shader does all the visual work.
    /// </summary>
    public class MazeRenderer : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject _wallPrefab;
        [SerializeField] private GameObject _floorPrefab;
        [SerializeField] private GameObject _collectiblePrefab;
        [SerializeField] private GameObject _powerPelletPrefab;
        [SerializeField] private GameObject _portalPrefab;

        [Header("Settings")]
        [SerializeField] private float _cellSize = 2f;
        [SerializeField] private float _wallHeight = 3f;

        private Transform _mazeParent;

        /// <summary>
        /// Build the maze from a generated grid.
        /// Called by GameManager when round starts.
        /// </summary>
        public void BuildMaze(int[,] grid, int width, int height)
        {
            ClearMaze();

            _mazeParent = new GameObject("Maze").transform;
            _mazeParent.SetParent(transform);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector3 worldPos = GridToWorld(x, y, width, height);

                    switch (grid[x, y])
                    {
                        case MazeGenerator.Wall:
                            SpawnWall(worldPos);
                            break;

                        case MazeGenerator.Corridor:
                        case MazeGenerator.Intersection:
                            SpawnFloor(worldPos);
                            break;

                        case MazeGenerator.Collectible:
                            SpawnFloor(worldPos);
                            SpawnCollectible(worldPos);
                            break;

                        case MazeGenerator.PowerPellet:
                            SpawnFloor(worldPos);
                            SpawnPowerPellet(worldPos);
                            break;

                        case MazeGenerator.Portal:
                            SpawnFloor(worldPos);
                            SpawnPortal(worldPos);
                            break;

                        case MazeGenerator.GhostSpawn:
                        case MazeGenerator.TargetSpawn:
                            SpawnFloor(worldPos);
                            break;
                    }
                }
            }
        }

        public void ClearMaze()
        {
            if (_mazeParent != null)
                Destroy(_mazeParent.gameObject);
        }

        private void SpawnWall(Vector3 pos)
        {
            GameObject wall;
            if (_wallPrefab != null)
            {
                wall = Instantiate(_wallPrefab, _mazeParent);
            }
            else
            {
                // Fallback: primitive cube (comes with BoxCollider)
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(_mazeParent);
            }

            wall.transform.position = pos + Vector3.up * (_wallHeight / 2f);
            wall.transform.localScale = new Vector3(_cellSize, _wallHeight, _cellSize);
            wall.isStatic = true;

            int wallLayer = LayerMask.NameToLayer("Wall");
            wall.layer = wallLayer >= 0 ? wallLayer : 0;

            // Ensure collider exists (prefab might not have one)
            if (wall.GetComponent<Collider>() == null)
                wall.AddComponent<BoxCollider>();
        }

        private void SpawnFloor(Vector3 pos)
        {
            GameObject floor;
            if (_floorPrefab != null)
            {
                floor = Instantiate(_floorPrefab, _mazeParent);
            }
            else
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
                floor.transform.SetParent(_mazeParent);
                floor.transform.rotation = Quaternion.Euler(90, 0, 0);
            }

            floor.transform.position = pos;
            floor.transform.localScale = new Vector3(_cellSize, _cellSize, 1);
        }

        private void SpawnCollectible(Vector3 pos)
        {
            GameObject collectible;
            if (_collectiblePrefab != null)
            {
                collectible = Instantiate(_collectiblePrefab, _mazeParent);
            }
            else
            {
                collectible = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                collectible.transform.SetParent(_mazeParent);
                collectible.transform.localScale = Vector3.one * 0.3f;
            }

            collectible.transform.position = pos + Vector3.up * 0.5f;
            collectible.tag = "Collectible";
        }

        private void SpawnPowerPellet(Vector3 pos)
        {
            GameObject pellet;
            if (_powerPelletPrefab != null)
            {
                pellet = Instantiate(_powerPelletPrefab, _mazeParent);
            }
            else
            {
                pellet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pellet.transform.SetParent(_mazeParent);
                pellet.transform.localScale = Vector3.one * 0.8f;
            }

            pellet.transform.position = pos + Vector3.up * 0.8f;
            pellet.tag = "PowerPellet";

            // Pulsing animation placeholder
            var pulse = pellet.AddComponent<PulseAnimation>();
            pulse.Speed = 2f;
            pulse.MinScale = 0.6f;
            pulse.MaxScale = 1.0f;
        }

        private void SpawnPortal(Vector3 pos)
        {
            GameObject portal;
            if (_portalPrefab != null)
            {
                portal = Instantiate(_portalPrefab, _mazeParent);
            }
            else
            {
                portal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                portal.transform.SetParent(_mazeParent);
                portal.transform.localScale = new Vector3(_cellSize * 0.8f, 0.1f, _cellSize * 0.8f);
            }

            portal.transform.position = pos + Vector3.up * 0.05f;
            portal.tag = "Portal";
        }

        private Vector3 GridToWorld(int x, int y, int width, int height)
        {
            return new Vector3(
                (x - width / 2f) * _cellSize,
                0,
                (y - height / 2f) * _cellSize
            );
        }
    }

    /// <summary>
    /// Simple scale pulse for power pellets.
    /// </summary>
    public class PulseAnimation : MonoBehaviour
    {
        public float Speed = 2f;
        public float MinScale = 0.6f;
        public float MaxScale = 1.0f;

        private void Update()
        {
            float t = (Mathf.Sin(Time.time * Speed) + 1f) / 2f;
            float scale = Mathf.Lerp(MinScale, MaxScale, t);
            transform.localScale = Vector3.one * scale;
        }
    }
}
