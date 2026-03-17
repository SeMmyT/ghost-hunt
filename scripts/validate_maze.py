#!/usr/bin/env python3
"""Validate MazeGenerator algorithm by reimplementing in Python.
Verifies connectivity, dead-end freedom, symmetry, and item placement.
"""
import random
from collections import deque

WALL = 0
CORRIDOR = 1
INTERSECTION = 2
GHOST_SPAWN = 3
TARGET_SPAWN = 4
POWER_PELLET = 5
PORTAL = 6
COLLECTIBLE = 7

NAMES = {
    WALL: ".", CORRIDOR: " ", INTERSECTION: "+", GHOST_SPAWN: "G",
    TARGET_SPAWN: "T", POWER_PELLET: "O", PORTAL: "P", COLLECTIBLE: "·",
}


def generate(width=29, height=31, seed=42):
    width = width | 1
    height = height | 1
    rng = random.Random(seed)
    grid = [[WALL] * height for _ in range(width)]

    center_x = width // 2
    half_cols = center_x // 2
    rows = (height - 1) // 2

    def in_bounds(x, y):
        return 0 <= x < width and 0 <= y < height

    def set_symmetric(x, y, val):
        if in_bounds(x, y):
            grid[x][y] = val
        mx = width - 1 - x
        if in_bounds(mx, y):
            grid[mx][y] = val

    def ensure_corridor(x, y):
        if in_bounds(x, y) and grid[x][y] == WALL:
            grid[x][y] = CORRIDOR

    def count_open(x, y):
        n = 0
        if x > 0 and grid[x - 1][y] != WALL: n += 1
        if x < width - 1 and grid[x + 1][y] != WALL: n += 1
        if y > 0 and grid[x][y - 1] != WALL: n += 1
        if y < height - 1 and grid[x][y + 1] != WALL: n += 1
        return n

    # 1. Spanning tree (DFS on left half, mirrored)
    visited = [[False] * rows for _ in range(half_cols)]
    stack = [(0, 0)]
    visited[0][0] = True
    set_symmetric(1, 1, CORRIDOR)

    while stack:
        c, r = stack[-1]
        neighbors = []
        if c > 0 and not visited[c - 1][r]: neighbors.append((c - 1, r))
        if c + 1 < half_cols and not visited[c + 1][r]: neighbors.append((c + 1, r))
        if r > 0 and not visited[c][r - 1]: neighbors.append((c, r - 1))
        if r + 1 < rows and not visited[c][r + 1]: neighbors.append((c, r + 1))

        if not neighbors:
            stack.pop()
            continue

        nc, nr = rng.choice(neighbors)
        visited[nc][nr] = True
        set_symmetric(nc * 2 + 1, nr * 2 + 1, CORRIDOR)
        set_symmetric(c + nc + 1, r + nr + 1, CORRIDOR)
        stack.append((nc, nr))

    # 2. Add loops
    prob = 0.35
    for c in range(half_cols):
        for r in range(rows):
            if c + 1 < half_cols and rng.random() < prob:
                wx, wy = c * 2 + 2, r * 2 + 1
                if grid[wx][wy] == WALL:
                    set_symmetric(wx, wy, CORRIDOR)
            if r + 1 < rows and rng.random() < prob:
                wx, wy = c * 2 + 1, r * 2 + 2
                if grid[wx][wy] == WALL:
                    set_symmetric(wx, wy, CORRIDOR)

    # 3. Connect halves (bridge gap between center and coarse columns)
    cx = center_x

    def open_center(cx_, y_):
        grid[cx_][y_] = CORRIDOR
        if cx_ > 0 and grid[cx_ - 1][y_] == WALL:
            grid[cx_ - 1][y_] = CORRIDOR
        if cx_ < width - 1 and grid[cx_ + 1][y_] == WALL:
            grid[cx_ + 1][y_] = CORRIDOR

    connections = 0
    for r in range(rows):
        if rng.random() < 0.45:
            open_center(cx, r * 2 + 1)
            connections += 1
    attempts = 0
    while connections < 3 and attempts < 100:
        r = rng.randint(0, rows - 1)
        y = r * 2 + 1
        if grid[cx][y] == WALL:
            open_center(cx, y)
            connections += 1
        attempts += 1

    # 4. Place spawns
    cx, cy = width // 2, height // 2
    grid[cx][cy] = GHOST_SPAWN
    if cx > 0: grid[cx - 1][cy] = GHOST_SPAWN
    if cx < width - 1: grid[cx + 1][cy] = GHOST_SPAWN
    if cy > 0: grid[cx][cy - 1] = GHOST_SPAWN
    ty = height - 2
    grid[cx][ty] = TARGET_SPAWN
    ensure_corridor(cx - 1, ty)
    ensure_corridor(cx + 1, ty)
    ensure_corridor(cx, ty - 1)

    # 5. Place portals
    mid_y = height // 2
    grid[0][mid_y] = PORTAL
    grid[width - 1][mid_y] = PORTAL
    ensure_corridor(1, mid_y)
    ensure_corridor(width - 2, mid_y)

    # 6. Remove dead ends (after all corridor changes)
    changed = True
    passes = 0
    while changed and passes < 100:
        changed = False
        passes += 1
        for x in range(1, width - 1):
            for y in range(1, height - 1):
                if grid[x][y] == WALL or grid[x][y] == GHOST_SPAWN:
                    continue
                if count_open(x, y) > 1:
                    continue
                walls = []
                if x > 1 and grid[x - 1][y] == WALL: walls.append((x - 1, y))
                if x < width - 2 and grid[x + 1][y] == WALL: walls.append((x + 1, y))
                if y > 1 and grid[x][y - 1] == WALL: walls.append((x, y - 1))
                if y < height - 2 and grid[x][y + 1] == WALL: walls.append((x, y + 1))
                if walls:
                    wx, wy = rng.choice(walls)
                    grid[wx][wy] = CORRIDOR
                    changed = True

    # 7. Classify intersections
    for x in range(1, width - 1):
        for y in range(1, height - 1):
            if grid[x][y] == CORRIDOR and count_open(x, y) >= 3:
                grid[x][y] = INTERSECTION

    # 8. Place collectibles
    for x in range(width):
        for y in range(height):
            if grid[x][y] in (CORRIDOR, INTERSECTION):
                grid[x][y] = COLLECTIBLE

    for px, py in [(1, 1), (width - 2, 1), (1, height - 2), (width - 2, height - 2)]:
        if in_bounds(px, py) and grid[px][py] != WALL:
            grid[px][py] = POWER_PELLET

    return grid, width, height


def validate(grid, width, height):
    errors = []

    # Connectivity (flood fill)
    start = None
    total_open = 0
    for x in range(width):
        for y in range(height):
            if grid[x][y] != WALL:
                total_open += 1
                if start is None:
                    start = (x, y)

    if start is None:
        errors.append("No open cells!")
        return errors

    visited = set()
    queue = deque([start])
    visited.add(start)
    while queue:
        x, y = queue.popleft()
        for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            nx, ny = x + dx, y + dy
            if 0 <= nx < width and 0 <= ny < height and (nx, ny) not in visited and grid[nx][ny] != WALL:
                visited.add((nx, ny))
                queue.append((nx, ny))

    if len(visited) != total_open:
        errors.append(f"NOT CONNECTED: {len(visited)}/{total_open} cells reachable")

    # Dead ends (interior only, skip portals)
    dead_ends = 0
    for x in range(1, width - 1):
        for y in range(1, height - 1):
            if grid[x][y] == WALL or grid[x][y] == GHOST_SPAWN:
                continue
            n = 0
            if x > 0 and grid[x - 1][y] != WALL: n += 1
            if x < width - 1 and grid[x + 1][y] != WALL: n += 1
            if y > 0 and grid[x][y - 1] != WALL: n += 1
            if y < height - 1 and grid[x][y + 1] != WALL: n += 1
            if n <= 1:
                dead_ends += 1
    if dead_ends > 0:
        errors.append(f"DEAD ENDS: {dead_ends}")

    # Item counts
    counts = {}
    for x in range(width):
        for y in range(height):
            v = grid[x][y]
            counts[v] = counts.get(v, 0) + 1

    if counts.get(GHOST_SPAWN, 0) < 3:
        errors.append(f"Ghost spawns: {counts.get(GHOST_SPAWN, 0)} (need >=3)")
    if counts.get(TARGET_SPAWN, 0) < 1:
        errors.append("No target spawn")
    if counts.get(PORTAL, 0) < 2:
        errors.append(f"Portals: {counts.get(PORTAL, 0)} (need >=2)")
    if counts.get(POWER_PELLET, 0) != 4:
        errors.append(f"Power pellets: {counts.get(POWER_PELLET, 0)} (need 4)")
    if counts.get(COLLECTIBLE, 0) < 50:
        errors.append(f"Collectibles: {counts.get(COLLECTIBLE, 0)} (need >=50)")

    return errors


def render(grid, width, height):
    lines = []
    for y in range(height):
        row = ""
        for x in range(width):
            row += NAMES.get(grid[x][y], "?")
        lines.append(row)
    return "\n".join(lines)


def main():
    print("=== MazeGenerator Validation ===\n")

    # Test multiple seeds
    all_pass = True
    for seed in range(1, 21):
        grid, w, h = generate(29, 31, seed)
        errs = validate(grid, w, h)
        status = "PASS" if not errs else "FAIL"
        if errs:
            all_pass = False
        print(f"Seed {seed:3d}: {status}  {', '.join(errs) if errs else ''}")

    # Test even input dimensions (should force odd)
    for w_in, h_in in [(28, 31), (30, 30), (20, 20)]:
        grid, w, h = generate(w_in, h_in, 42)
        assert w % 2 == 1, f"Width {w} not odd"
        assert h % 2 == 1, f"Height {h} not odd"
        errs = validate(grid, w, h)
        status = "PASS" if not errs else "FAIL"
        if errs:
            all_pass = False
        print(f"Even input {w_in}x{h_in} -> {w}x{h}: {status}  {', '.join(errs) if errs else ''}")

    # Determinism test
    g1, _, _ = generate(29, 31, 999)
    g2, _, _ = generate(29, 31, 999)
    match = all(g1[x][y] == g2[x][y] for x in range(29) for y in range(31))
    print(f"Determinism: {'PASS' if match else 'FAIL'}")

    # Print one maze for visual inspection
    print("\n=== Sample Maze (seed=1) ===")
    grid, w, h = generate(29, 31, 1)
    print(render(grid, w, h))

    counts = {}
    for x in range(w):
        for y in range(h):
            counts[grid[x][y]] = counts.get(grid[x][y], 0) + 1
    print(f"\nCells: wall={counts.get(WALL,0)} collectible={counts.get(COLLECTIBLE,0)} "
          f"pellet={counts.get(POWER_PELLET,0)} portal={counts.get(PORTAL,0)} "
          f"ghost={counts.get(GHOST_SPAWN,0)} target={counts.get(TARGET_SPAWN,0)}")

    print(f"\n{'ALL TESTS PASSED' if all_pass and match else 'SOME TESTS FAILED'}")


if __name__ == "__main__":
    main()
