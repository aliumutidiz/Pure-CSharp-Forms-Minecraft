// Core namespaces for Windows Forms, Drawing, and 3D vector math
using System;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ProgressBar;
using Timer = System.Windows.Forms.Timer;

using System.Numerics;

namespace Minecraft
{
    public partial class Form1 : Form
    {
        #region Fields

        // Timer for main update loop
        private Timer timer = new Timer();

        // Camera and movement control
        private Vector3 cameraPosition = new Vector3(0, 370, 0);
        private Vector3 cameraVelocity = Vector3.Zero;
        private float yaw = 0, pitch = 0;
        private bool[] keys = new bool[256];
        private bool onGround = false;
        private Point lastMousePos = Point.Empty;
        private DateTime lastFrame = DateTime.Now;
        private bool mouseCentered = false;

        // Rendering options
        private bool wireframe = false;
        private bool showBlockFaces = false;

        // Block & player configuration
        private const float blockSize = 30f;
        private const float playerHeight = 100f;
        private const float gravity = 300f;
        private const float jumpVelocity = 170.8f;

        // Block storage
        private List<Vector3> placedBlocks = new List<Vector3>();
        private List<Vector3> justPlacedBlocks = new();
        private List<Vector3> justRemovedBlocks = new();
        private Dictionary<(int, int), float> terrainHeights = new();
        private Dictionary<Vector3, Color> blockColors = new();
        private Color currentBlockColor = Color.SaddleBrown;

        // Lighting and highlighting
        private static Vector3 lightDirection = new Vector3(1, 1, 1);
        private Vector3? highlightedBlock = null;

        // Performance
        private int frameCount = 0;
        private int fps = 0;
        private DateTime lastFpsTime = DateTime.Now;

        // Visual toggles
        private bool isBackgroundSkyBlue = false;
        private bool wasBPressed = false;

        // Lighting
        private Vector3 sunDirection = lightDirection;

        // Texture
        private Bitmap textureAtlas;
        private bool useTextures = true;

        // Chunk system
        private const int MaxVisibleBlocks = 99999;
        private const int ChunkSize = 16;
        private Dictionary<(int, int), Chunk> chunks = new();

        // Cube face data
        private int[][] faces = {
            new[]{6,7,3,2}, new[]{4,5,1,0}, new[]{7,6,5,4},
            new[]{3,7,4,0}, new[]{5,6,2,1}, new[]{1,2,3,0}
        };

        private Vector3[] faceNormals;
        private Color[] faceColors;
        private string[] faceLabels = { "Top", "Bottom", "Front", "Left", "Right", "Back" };

        private int chunkRange = 4;
        private bool blockLines = true;

        #endregion

        #region Initialization

        public Form1()
        {
            InitializeComponent();

            this.DoubleBuffered = true;
            this.BackColor = Color.SkyBlue;
            this.KeyPreview = true;
            
            this.MouseDown += Form1_MouseDown;
            this.MouseMove += Form1_MouseMove;

            this.KeyDown += (s, e) =>
            {
                keys[e.KeyValue] = true;

                if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)
                    chunkRange = Math.Min(chunkRange + 1, 150);
                else if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)
                    chunkRange = Math.Max(chunkRange - 1, 1);
                else if (e.KeyCode == Keys.X)
                    blockLines = !blockLines;
            };

            this.KeyUp += (s, e) => keys[e.KeyValue] = false;

            timer.Interval = 16;
            timer.Tick += Timer_Tick;
            timer.Start();

            Cursor.Hide();

            // Optional world generation: flat terrain or maze
            // for (int x = -10; x <= 100; x++)
            // {
            //     for (int z = -10; z <= 100; z++)
            //     {
            //         Random rnd = new Random();
            //         Color rndclr = Color.FromArgb(rnd.Next(1, 255), rnd.Next(1, 255), rnd.Next(1, 255));
            //         float y = 0f;
            //         terrainHeights[(x, z)] = y;
            //         var pos = new Vector3(x * blockSize, y, z * blockSize);
            //         AddBlock(x, 0, z);
            //     }
            // }

            // LoadMap.LoadMapFromFile(this); // ← Uncomment to load a map
             Maze.BuildMazeFromFile(this); // ← Currently used for testing

            lightDirection = Normalize(lightDirection);

            // Setup cube face normals for lighting calculations
            float[,] cube = {
                {0,0,0},{1,0,0},{1,1,0},{0,1,0},
                {0,0,1},{1,0,1},{1,1,1},{0,1,1}
            };

            faceNormals = new Vector3[6];
            for (int f = 0; f < 6; f++)
            {
                int[] face = faces[f];
                Vector3 v0 = new Vector3(cube[face[0], 0], cube[face[0], 1], cube[face[0], 2]);
                Vector3 v1 = new Vector3(cube[face[1], 0], cube[face[1], 1], cube[face[1], 2]);
                Vector3 v2 = new Vector3(cube[face[2], 0], cube[face[2], 1], cube[face[2], 2]);
                Vector3 edge1 = v1 - v0;
                Vector3 edge2 = v2 - v0;
                faceNormals[f] = Normalize(Cross(edge1, edge2));
            }

            // Lighting color setup per face
            Vector3[] outwardNormals = new Vector3[]
            {
                new Vector3(0,1,0),   // Top
                new Vector3(0,-1,0),  // Bottom
                new Vector3(0,0,1),   // Front
                new Vector3(-1,0,0),  // Left
                new Vector3(1,0,0),   // Right
                new Vector3(0,0,-1)   // Back
            };

            faceColors = new Color[6];
            for (int i = 0; i < 6; i++)
            {
                float light = Math.Max(Vector3.Dot(lightDirection, outwardNormals[i]), 0f);
                int brightness = (int)(50 + light * 180);
                faceColors[i] = Color.FromArgb(brightness, brightness, brightness);
            }
        }

        #endregion

        #region Timer and Input Events

        private void Timer_Tick(object sender, EventArgs e)
        {
            float deltaTime = (float)(DateTime.Now - lastFrame).TotalSeconds;
            lastFrame = DateTime.Now;
            UpdateCamera(deltaTime);
            this.Invalidate(); // Triggers repaint
        }

        private void Form1_MouseMove(object sender, MouseEventArgs e)
        {
            if (!mouseCentered) { CenterMouse(); return; }
            float dx = e.X - lastMousePos.X;
            float dy = e.Y - lastMousePos.Y;
            yaw += dx * 0.005f;
            pitch = Math.Clamp(pitch + dy * 0.005f, -1.55f, 1.55f);
            CenterMouse();
        }

        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && highlightedBlock.HasValue)
            {
                var pos = highlightedBlock.Value;
                int bx = (int)Math.Floor(pos.X / blockSize);
                int by = (int)Math.Floor(pos.Y / blockSize);
                int bz = (int)Math.Floor(pos.Z / blockSize);
                RemoveBlock(bx, by, bz);
            }
            else if (e.Button == MouseButtons.Right)
            {
                var (hitBlock, faceNormal) = RaycastHitBlock(cameraPosition, GetLookDirection() * -1, 300f);

                if (hitBlock.HasValue && faceNormal.HasValue)
                {
                    Vector3 placePos = hitBlock.Value + faceNormal.Value * blockSize;
                    placePos = AlignToGrid(placePos);

                    if (!IsBlockOccupied(placePos))
                    {
                        int bx = (int)Math.Floor(placePos.X / blockSize);
                        int by = (int)Math.Floor(placePos.Y / blockSize);
                        int bz = (int)Math.Floor(placePos.Z / blockSize);
                        AddBlock(bx, by, bz, currentBlockColor);
                    }
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                wireframe = !wireframe;
            }
        }

        #endregion

        #region Collision and World Helpers

        private bool IsBlockOccupied(Vector3 center)
        {
            foreach (var chunk in GetNearbyChunks(center))
                if (chunk.Blocks.Contains(center)) return true;
            return false;
        }

        private Vector3 GetHitFaceDirection(Vector3 origin, Vector3 blockPos)
        {
            Vector3 relative = Normalize(blockPos + new Vector3(blockSize / 2, blockSize / 2, blockSize / 2) - origin);

            Vector3[] directions = {
                new Vector3(1,0,0), new Vector3(-1,0,0),
                new Vector3(0,1,0), new Vector3(0,-1,0),
                new Vector3(0,0,1), new Vector3(0,0,-1)
            };

            float maxDot = -1;
            Vector3 best = Vector3.Zero;

            foreach (var dir in directions)
            {
                float dot = Vector3.Dot(dir, relative);
                if (dot > maxDot)
                {
                    maxDot = dot;
                    best = dir;
                }
            }

            return best;
        }

        private void CenterMouse()
        {
            Point center = new Point(this.ClientSize.Width / 2, this.ClientSize.Height / 2);
            Cursor.Position = this.PointToScreen(center);
            lastMousePos = center;
            mouseCentered = true;
        }

        private bool IsBoxColliding(Vector3 center)
        {
            float halfPlayerWidth = blockSize * 0.3f;
            float halfHeight = playerHeight / 2f;
            float tolerance = 0.001f;

            Vector3 min = center - new Vector3(halfPlayerWidth - tolerance, halfHeight - tolerance, halfPlayerWidth - tolerance);
            Vector3 max = center + new Vector3(halfPlayerWidth - tolerance, halfHeight - tolerance, halfPlayerWidth - tolerance);

            var pq = new PriorityQueue<Vector3, float>();
            foreach (var chunk in GetNearbyChunks(center))
                foreach (var b in chunk.Blocks)
                    pq.Enqueue(b, Vector3.DistanceSquared(b, center));

            while (pq.Count > 0)
            {
                var b = pq.Dequeue();
                Vector3 bmin = b - new Vector3(blockSize / 2f);
                Vector3 bmax = b + new Vector3(blockSize / 2f);

                if (min.X < bmax.X && max.X > bmin.X &&
                    min.Y < bmax.Y && max.Y > bmin.Y &&
                    min.Z < bmax.Z && max.Z > bmin.Z)
                    return true;
            }

            return false;
        }

        #endregion


        #region Camera Update and Movement

        /// <summary>
        /// Updates the camera position, processes input, applies gravity and handles collisions.
        /// </summary>
        private void UpdateCamera(float dt)
        {
            // Get forward and right directions based on current yaw
            Vector3 forward = GetLookDirection();
            Vector3 right = new Vector3((float)Math.Sin(yaw - Math.PI / 2), 0, (float)Math.Cos(yaw - Math.PI / 2));
            float speed = 150f * dt;

            // Handle directional input
            Vector3 movement = Vector3.Zero;
            if (keys['W']) movement -= new Vector3(forward.X, 0, forward.Z) * speed;
            if (keys['S']) movement += new Vector3(forward.X, 0, forward.Z) * speed;
            if (keys['A']) movement -= right * speed;
            if (keys['D']) movement += right * speed;

            // Toggle background color (sky/black) on 'B' key press
            if (keys['B'] && !wasBPressed)
            {
                isBackgroundSkyBlue = !isBackgroundSkyBlue;
                this.BackColor = isBackgroundSkyBlue ? Color.SkyBlue : Color.Black;
                wasBPressed = true;
            }
            else if (!keys['B'])
            {
                wasBPressed = false;
            }

            // X and Z axis collision check
            Vector3 newPos = cameraPosition;
            Vector3 tryX = new Vector3(newPos.X + movement.X, newPos.Y, newPos.Z);
            if (!IsBoxColliding(tryX)) newPos.X += movement.X;

            Vector3 tryZ = new Vector3(newPos.X, newPos.Y, newPos.Z + movement.Z);
            if (!IsBoxColliding(tryZ)) newPos.Z += movement.Z;

            // Toggle block face visibility on 'Z' key press
            if (keys['Z']) showBlockFaces = !showBlockFaces;

            // Apply gravity and Y-axis collision
            cameraVelocity.Y -= gravity * dt;
            float deltaY = cameraVelocity.Y * dt;
            Vector3 tryY = new Vector3(newPos.X, newPos.Y + deltaY, newPos.Z);
            if (!IsBoxColliding(tryY))
                newPos.Y += deltaY;
            else
                cameraVelocity.Y = 0;

            cameraPosition = newPos;

            // Try to escape inside-block collision (move up)
            for (int i = 0; i < 20 && IsBoxColliding(cameraPosition); i++)
                cameraPosition.Y += 1f;

            // Ground check (standing on top of a block)
            float feetY = cameraPosition.Y - playerHeight / 2f;
            bool standing = false;

            var pq = new PriorityQueue<Vector3, float>();
            foreach (var chunk in GetNearbyChunks(cameraPosition))
                foreach (var b in chunk.Blocks)
                    pq.Enqueue(b, (b - cameraPosition).LengthSquared());

            while (pq.Count > 0)
            {
                var b = pq.Dequeue();
                float bTop = b.Y + blockSize / 2f;
                float bX = b.X, bZ = b.Z;
                float px = cameraPosition.X, pz = cameraPosition.Z;

                bool onTop = Math.Abs(feetY - bTop) < 3f &&
                             Math.Abs(px - bX) < blockSize * 0.5f &&
                             Math.Abs(pz - bZ) < blockSize * 0.5f;

                if (onTop)
                {
                    standing = true;
                    cameraPosition.Y = bTop + playerHeight / 2f;
                    cameraVelocity.Y = 0;
                    break;
                }
            }

            onGround = standing;

            // Jump if on ground and space key is pressed
            if (keys[(int)Keys.Space] && onGround)
            {
                cameraVelocity.Y = jumpVelocity;
                onGround = false;
            }

            // Reset if fallen below world
            if (cameraPosition.Y < -1000)
            {
                cameraPosition = new Vector3(0, 300, 0);
                cameraVelocity = Vector3.Zero;
            }

            // Raycast to detect currently highlighted block
            var (hitBlock, faceNormal) = RaycastHitBlock(cameraPosition, GetLookDirection() * -1, 300f);
            highlightedBlock = hitBlock;
        }

        #endregion

        #region Block Manipulation

        /// <summary>
        /// Adds a new block at the specified block coordinates with optional color.
        /// </summary>
        public void AddBlock(int bx, int by, int bz, Color? color = null)
        {
            float bs = blockSize;
            Vector3 aligned = new Vector3(bx * bs, by * bs, bz * bs);
            var (cx, cz) = GetChunkCoord(aligned);
            var chunk = GetChunk(cx, cz);
            if (chunk.Blocks.Contains(aligned)) return;
            chunk.Blocks.Add(aligned);
            if (color.HasValue)
                chunk.Colors[aligned] = color.Value;
        }

        /// <summary>
        /// Removes a block at the specified block coordinates.
        /// </summary>
        public void RemoveBlock(int bx, int by, int bz)
        {
            float bs = blockSize;
            Vector3 aligned = new Vector3(bx * bs, by * bs, bz * bs);
            var (cx, cz) = GetChunkCoord(aligned);
            if (!chunks.TryGetValue((cx, cz), out var chunk)) return;
            if (chunk.Blocks.Remove(aligned))
                chunk.Colors.Remove(aligned);
        }

        #endregion

        #region UI Rendering

        /// <summary>
        /// Draws a red crosshair in the center of the screen.
        /// </summary>
        private void DrawCrosshair(Graphics g, int width, int height)
        {
            int size = 5;
            Pen pen = Pens.Red;

            g.DrawLine(pen, width / 2 - size, height / 2, width / 2 + size, height / 2);
            g.DrawLine(pen, width / 2, height / 2 - size, width / 2, height / 2 + size);
        }

        #endregion

        #region Raycasting

        /// <summary>
        /// Performs a raycast to find the first intersected block and the hit face normal.
        /// </summary>
        private (Vector3? block, Vector3? faceNormal) RaycastHitBlock(Vector3 origin, Vector3 dir, float maxDistance)
        {
            dir = Vector3.Normalize(dir);
            float step = blockSize / 20f;

            var pq = new PriorityQueue<Vector3, float>();
            foreach (var chunk in GetNearbyChunks(origin))
            {
                foreach (var b in chunk.Blocks)
                {
                    float distSq = Vector3.DistanceSquared(b, origin);
                    pq.Enqueue(b, distSq);
                }
            }

            while (pq.Count > 0)
            {
                Vector3 b = pq.Dequeue();
                Vector3 min = b - new Vector3(blockSize / 2f);
                Vector3 max = b + new Vector3(blockSize / 2f);

                for (float t = 0; t < maxDistance; t += step)
                {
                    Vector3 point = origin + dir * t;

                    if (point.X >= min.X && point.X <= max.X &&
                        point.Y >= min.Y && point.Y <= max.Y &&
                        point.Z >= min.Z && point.Z <= max.Z)
                    {
                        Vector3 center = (min + max) / 2f;
                        Vector3 delta = point - center;
                        Vector3 abs = new Vector3(Math.Abs(delta.X), Math.Abs(delta.Y), Math.Abs(delta.Z));

                        float maxAbs = Math.Max(abs.X, Math.Max(abs.Y, abs.Z));

                        Vector3 normal;
                        if (maxAbs == abs.X)
                            normal = delta.X > 0 ? new Vector3(1, 0, 0) : new Vector3(-1, 0, 0);
                        else if (maxAbs == abs.Y)
                            normal = delta.Y > 0 ? new Vector3(0, 1, 0) : new Vector3(0, -1, 0);
                        else
                            normal = delta.Z > 0 ? new Vector3(0, 0, 1) : new Vector3(0, 0, -1);

                        return (b, normal);
                    }
                }
            }

            return (null, null);
        }

        #endregion

        #region Utility

        /// <summary>
        /// Aligns the given world position to the block grid.
        /// </summary>
        private Vector3 AlignToGrid(Vector3 pos)
        {
            return new Vector3(
                (float)Math.Floor(pos.X / blockSize) * blockSize,
                (float)Math.Floor(pos.Y / blockSize) * blockSize,
                (float)Math.Floor(pos.Z / blockSize) * blockSize);
        }

        /// <summary>
        /// Checks if a ray intersects a block's bounding box.
        /// </summary>
        private bool RayIntersectsBlock(Vector3 origin, Vector3 dir, Vector3 blockPos, float blockSize)
        {
            float tMin = 0;
            float tMax = 1000f;

            for (int i = 0; i < 3; i++)
            {
                float o = i == 0 ? origin.X : i == 1 ? origin.Y : origin.Z;
                float d = i == 0 ? dir.X : i == 1 ? dir.Y : dir.Z;
                float min = i == 0 ? blockPos.X : i == 1 ? blockPos.Y : blockPos.Z;
                float max = min + blockSize;

                if (Math.Abs(d) < 0.0001f)
                {
                    if (o < min || o > max) return false;
                }
                else
                {
                    float t1 = (min - o) / d;
                    float t2 = (max - o) / d;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tMin = Math.Max(tMin, t1);
                    tMax = Math.Min(tMax, t2);
                    if (tMin > tMax) return false;
                }
            }

            return true;
        }

        #endregion


        #region Rendering

        /// <summary>
        /// Main paint method. Renders all visible blocks, HUD info, crosshair and FPS.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Matrix4x4 view = GetViewMatrix(cameraPosition);
            Graphics g = e.Graphics;

            // Collect nearby visible blocks
            var pq = new PriorityQueue<(float x, float y, float z), float>();
            Vector3 look = GetLookDirection();
            float maxDistSq = (chunkRange * blockSize) * (chunkRange * blockSize);

            foreach (var chunk in GetNearbyChunks(cameraPosition))
            {
                foreach (var b in chunk.Blocks)
                {
                    Vector3 toBlock = (b - cameraPosition) * -1;
                    float distSq = toBlock.LengthSquared();
                    float dot = Vector3.Dot(look, Normalize(toBlock));
                    if (dot < 0.3f) continue;
                    pq.Enqueue((b.X, b.Y, b.Z), -distSq);
                }
            }

            // Sort and draw
            List<(float x, float y, float z)> sortedCubes = new();
            while (pq.Count > 0 && sortedCubes.Count < MaxVisibleBlocks)
                sortedCubes.Add(pq.Dequeue());

            foreach (var cube in sortedCubes)
                DrawCube(g, cube.x, cube.y, cube.z, blockSize, view);

            // Debug information
            g.DrawString($"Ray origin: {cameraPosition.X:F1},{cameraPosition.Y:F1},{cameraPosition.Z:F1}",
                    new Font("Arial", 8), Brushes.White, 10, 40);

            if (highlightedBlock.HasValue)
            {
                var b = highlightedBlock.Value;
                g.DrawString($"Hit: {b.X},{b.Y},{b.Z}", new Font("Arial", 8), Brushes.Lime, 10, 60);
            }

            g.DrawString($"Grounded: {onGround}", new Font("Arial", 10, FontStyle.Bold),
                onGround ? Brushes.Lime : Brushes.Red, new PointF(10, 80));

            // FPS counter
            frameCount++;
            if ((DateTime.Now - lastFpsTime).TotalSeconds >= 1)
            {
                fps = frameCount;
                frameCount = 0;
                lastFpsTime = DateTime.Now;
            }

            g.DrawString($"FPS: {fps}", new Font("Consolas", 10), Brushes.Orange, 10, 120);

            // Crosshair and position
            DrawCrosshair(g, this.Width, this.Height);

            int cx = (int)Math.Floor(cameraPosition.X / blockSize);
            int cy = (int)Math.Floor(cameraPosition.Y / blockSize);
            int cz = (int)Math.Floor(cameraPosition.Z / blockSize);

            g.DrawString($"Coord: [{cx}, {cy}, {cz}]",
                new Font("Consolas", 10), Brushes.White, 10, 100);
        }

        /// <summary>
        /// Draws a single cube block with lighting and optional wireframe.
        /// </summary>
        private void DrawCube(Graphics g, float x, float y, float z, float size, Matrix4x4 view)
        {
            x -= size / 2f;
            y -= size / 2f;
            z -= size / 2f;

            Vector3[] cubeCorners = new Vector3[8];
            float[,] cube = {
        {0,0,0},{1,0,0},{1,1,0},{0,1,0},
        {0,0,1},{1,0,1},{1,1,1},{0,1,1}
    };

            for (int i = 0; i < 8; i++)
                cubeCorners[i] = new Vector3(x + cube[i, 0] * size, y + cube[i, 1] * size, z + cube[i, 2] * size);

            PointF[] projected = ProjectCube(x, y, z, size, view);
            var pq = new PriorityQueue<int, float>();

            for (int f = 0; f < 6; f++)
            {
                Vector3 v0 = cubeCorners[faces[f][0]];
                Vector3 v2 = cubeCorners[faces[f][2]];
                Vector3 center = (v0 + v2) / 2f;
                float distSq = (cameraPosition - center).LengthSquared();
                pq.Enqueue(f, -distSq);
            }

            while (pq.Count > 0)
            {
                int f = pq.Dequeue();
                Vector3 faceNormal = faceNormals[f];
                Vector3 v0 = cubeCorners[faces[f][0]];
                Vector3 v2 = cubeCorners[faces[f][2]];
                Vector3 faceCenter = (v0 + v2) / 2f;
                Vector3 toCamera = Normalize(cameraPosition - faceCenter);

                if (Vector3.Dot(faceNormal, toCamera) > 0f) continue;

                PointF[] pts = faces[f].Select(i => projected[i]).ToArray();
                Vector3 centerPos = new Vector3(x + size / 2f, y + size / 2f, z + size / 2f);
                Color? color = GetBlockColor(centerPos);

                if (!wireframe)
                {
                    if (!color.HasValue)
                    {
                        color = faceColors[f];
                    }
                    else
                    {
                        float ambient = 0.25f;
                        float dot = Vector3.Dot(Normalize(lightDirection * -1), faceNormals[f]);
                        float light = Math.Max(dot, 0f);
                        light = ambient + light * (1f - ambient);
                        Color baseColor = color.Value;
                        color = Color.FromArgb(
                            Clamp((int)(baseColor.R * light), 0, 255),
                            Clamp((int)(baseColor.G * light), 0, 255),
                            Clamp((int)(baseColor.B * light), 0, 255));
                    }

                    using var brush = new SolidBrush(color.Value);
                    g.FillPolygon(brush, pts);

                    if (blockLines)
                        g.DrawPolygon(Pens.Black, pts);
                }
                else
                {
                    g.DrawPolygon(Pens.Black, pts);
                }

                bool isHighlighted = highlightedBlock.HasValue &&
                    Math.Abs(x + size / 2f - highlightedBlock.Value.X) < 0.01f &&
                    Math.Abs(y + size / 2f - highlightedBlock.Value.Y) < 0.01f &&
                    Math.Abs(z + size / 2f - highlightedBlock.Value.Z) < 0.01f;

                if (isHighlighted)
                    g.DrawPolygon(new Pen(Color.Yellow, 3), pts);
            }
        }

        #endregion


        #region Utilities

        /// Checks if a block face is hidden behind another block
        private bool IsFaceOccluded(Vector3 faceCenter)
        {
            Vector3 dir = Normalize(faceCenter - cameraPosition);
            float distance = (faceCenter - cameraPosition).Length();

            foreach (var b in placedBlocks)
            {
                if (RayIntersectsBlock(cameraPosition, dir, b, blockSize))
                {
                    float blockDist = (b - cameraPosition).Length();
                    if (blockDist < distance - blockSize * 0.5f)
                        return true; // Block in the way
                }
            }

            return false;
        }

        /// Clamps value between min and max
        private int Clamp(int val, int min, int max) => Math.Max(min, Math.Min(max, val));

        /// Backface culling using screen-space cross product
        private bool IsFaceVisible(PointF a, PointF b, PointF c)
        {
            float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            return cross > 0;
        }

        /// Projects 3D cube to 2D screen
        private PointF[] ProjectCube(float x, float y, float z, float size, Matrix4x4 view)
        {
            PointF[] projected = new PointF[8];
            float[,] cube = {
        {0,0,0},{1,0,0},{1,1,0},{0,1,0},
        {0,0,1},{1,0,1},{1,1,1},{0,1,1}
    };
            for (int i = 0; i < 8; i++)
            {
                Vector3 world = new Vector3(x + cube[i, 0] * size, y + cube[i, 1] * size, z + cube[i, 2] * size);
                Vector3 cam = Transform(world - cameraPosition, view);
                if (cam.Z <= 0.3f)
                    return Enumerable.Repeat(new PointF(-9999, -9999), 8).ToArray();

                float scale = 600 / cam.Z;
                projected[i] = new PointF(
                    this.ClientSize.Width / 2 + cam.X * scale,
                    this.ClientSize.Height / 2 - cam.Y * scale);
            }
            return projected;
        }

        #endregion

        #region Camera and View

        /// Gets camera look direction based on pitch and yaw
        private Vector3 GetLookDirection()
        {
            float x = (float)(Math.Cos(pitch) * Math.Sin(yaw));
            float y = (float)(Math.Sin(pitch));
            float z = (float)(Math.Cos(pitch) * Math.Cos(yaw));
            return Vector3.Normalize(new Vector3(x, y, z));
        }

        /// Builds view matrix for transforming world to camera space
        private Matrix4x4 GetViewMatrix(Vector3 pos)
        {
            Vector3 target = pos + GetLookDirection();
            Vector3 up = new(0, 1, 0);
            Vector3 zaxis = Vector3.Normalize(pos - target);
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(up, zaxis));
            Vector3 yaxis = Vector3.Cross(zaxis, xaxis);

            return new Matrix4x4(
                xaxis.X, xaxis.Y, xaxis.Z, 0,
                yaxis.X, yaxis.Y, yaxis.Z, 0,
                zaxis.X, zaxis.Y, zaxis.Z, 0,
                0, 0, 0, 1);
        }

        #endregion

        #region Math Helpers

        private Vector3 Cross(Vector3 a, Vector3 b) => Vector3.Cross(a, b);
        private Vector3 Normalize(Vector3 v) => Vector3.Normalize(v);

        /// Applies rotation matrix to a vector (ignores translation)
        private Vector3 Transform(Vector3 v, Matrix4x4 m)
        {
            return new Vector3(
                v.X * m.M11 + v.Y * m.M12 + v.Z * m.M13,
                v.X * m.M21 + v.Y * m.M22 + v.Z * m.M23,
                v.X * m.M31 + v.Y * m.M32 + v.Z * m.M33);
        }

        #endregion

        #region Sun Drawing

        /// Draws a glowing sun based on light direction
        private void DrawSun3D(Graphics g)
        {
            Vector3 sunDir = Normalize(sunDirection);
            Vector3 sunWorld = cameraPosition + sunDir * 10000f;
            Matrix4x4 view = GetViewMatrix(cameraPosition);
            Vector3 sunCam = Transform(sunWorld - cameraPosition, view);

            if (sunCam.Z <= 1f) return;

            float scale = 600f / sunCam.Z;
            PointF screen = new PointF(
                this.ClientSize.Width / 2 + sunCam.X * scale,
                this.ClientSize.Height / 2 - sunCam.Y * scale);

            float radius = 50f;

            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(screen.X - radius, screen.Y - radius, radius * 2, radius * 2);

            using var gradient = new System.Drawing.Drawing2D.PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(255, 255, 255, 200),
                SurroundColors = new[] { Color.FromArgb(0, 255, 200, 0) },
                CenterPoint = screen
            };

            g.FillEllipse(gradient, screen.X - radius, screen.Y - radius, radius * 2, radius * 2);

            float inner = radius * 0.4f;
            using var coreBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 220));
            g.FillEllipse(coreBrush, screen.X - inner, screen.Y - inner, inner * 2, inner * 2);
        }

        #endregion

        #region World / Chunk Management

        private void Form1_Load(object sender, EventArgs e)
        {
            // You can initialize stuff here
        }

        /// Converts world position to chunk coordinates
        private (int, int) GetChunkCoord(Vector3 pos)
        {
            int cx = (int)Math.Floor(pos.X / (ChunkSize * blockSize));
            int cz = (int)Math.Floor(pos.Z / (ChunkSize * blockSize));
            return (cx, cz);
        }

        /// Gets or creates chunk at given position
        private Chunk GetChunk(int cx, int cz, bool createIfMissing = true)
        {
            if (!chunks.TryGetValue((cx, cz), out var chunk) && createIfMissing)
            {
                chunk = new Chunk();
                chunks[(cx, cz)] = chunk;
            }
            return chunk;
        }

        /// Gets all chunks around a center position
        private IEnumerable<Chunk> GetNearbyChunks(Vector3 center)
        {
            var (cx, cz) = GetChunkCoord(center);
            for (int dx = -chunkRange; dx <= chunkRange; dx++)
                for (int dz = -chunkRange; dz <= chunkRange; dz++)
                    if (chunks.TryGetValue((cx + dx, cz + dz), out var chunk))
                        yield return chunk;
        }

        /// Gets block color if assigned
        private Color? GetBlockColor(Vector3 pos)
        {
            var (cx, cz) = GetChunkCoord(pos);
            if (chunks.TryGetValue((cx, cz), out var chunk) && chunk.Colors.TryGetValue(pos, out var col))
                return col;
            return null;
        }

        /// Simple chunk class
        public class Chunk
        {
            public HashSet<Vector3> Blocks = new();
            public Dictionary<Vector3, Color> Colors = new();
        }

        #endregion
    }
}