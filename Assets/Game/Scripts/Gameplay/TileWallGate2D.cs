using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CGJ2026.Gameplay
{
    public sealed class TileWallGate2D : MonoBehaviour
    {
        public enum TileSelectionMode
        {
            WholeTilemap,
            ManualCells,
            RectAreas,
            ButtonWallChannel
        }

        [Serializable]
        public struct TileRect
        {
            [InspectorName("左下角格子坐标")] public Vector3Int origin;
            [InspectorName("宽度")] public int width;
            [InspectorName("高度")] public int height;
        }

        [Header("墙壁 Tilemap")]
        [SerializeField, InspectorName("目标 Tilemap")] private Tilemap tilemap;
        [SerializeField, InspectorName("选择方式")] private TileSelectionMode selectionMode = TileSelectionMode.WholeTilemap;
        [SerializeField, InspectorName("按钮墙砖通道")] private ButtonWallChannel wallChannel = ButtonWallChannel.Button1;
        [SerializeField, InspectorName("单独格子")] private Vector3Int[] cells;
        [SerializeField, InspectorName("矩形区域")] private TileRect[] areas;
        [SerializeField, InspectorName("启动时记录原始墙块")] private bool cacheOnAwake = true;

        [Header("开关行为")]
        [SerializeField, InspectorName("打开时隐藏墙块")] private bool clearTilesWhenOpen = true;
        [SerializeField, InspectorName("允许关闭后恢复")] private bool restoreTilesWhenClosed;

        [Header("消失表现")]
        [SerializeField, InspectorName("使用碎裂消失")] private bool useBreakEffect = true;
        [SerializeField, InspectorName("碎裂时间")] private float breakDuration = 0.38f;
        [SerializeField, InspectorName("碎片散开距离")] private float shardDistance = 0.38f;
        [SerializeField, InspectorName("碎片上抛高度")] private float shardLift = 0.24f;
        [SerializeField, InspectorName("碎片缩放")] private float shardScale = 0.48f;

        private readonly Dictionary<Vector3Int, TileState> originalTiles = new Dictionary<Vector3Int, TileState>();
        private Coroutine breakRoutine;
        private bool isOpen;

        public bool IsOpen => isOpen;

        private void Awake()
        {
            if (cacheOnAwake)
            {
                CacheTiles();
            }
        }

        public void Open()
        {
            SetOpen(true);
        }

        public void Close()
        {
            SetOpen(false);
        }

        public void SetOpen(bool open)
        {
            if (isOpen == open || tilemap == null)
            {
                return;
            }

            if (originalTiles.Count == 0)
            {
                CacheTiles();
            }

            isOpen = open;
            if (isOpen)
            {
                if (clearTilesWhenOpen)
                {
                    ClearSelectedTiles();
                }
            }
            else if (restoreTilesWhenClosed)
            {
                foreach (KeyValuePair<Vector3Int, TileState> entry in originalTiles)
                {
                    tilemap.SetTile(entry.Key, entry.Value.Tile);
                    tilemap.SetTransformMatrix(entry.Key, entry.Value.Transform);
                    tilemap.SetColor(entry.Key, entry.Value.Color);
                }
            }

            RefreshTilemapCollision();
        }

        private void ClearSelectedTiles()
        {
            List<Vector3Int> selectedCells = new List<Vector3Int>(EnumerateUniqueCells());
            if (useBreakEffect && Application.isPlaying && breakDuration > 0f)
            {
                if (breakRoutine != null)
                {
                    StopCoroutine(breakRoutine);
                }

                breakRoutine = StartCoroutine(BreakTilesThenClear(selectedCells));
                return;
            }

            for (int i = 0; i < selectedCells.Count; i++)
            {
                tilemap.SetTile(selectedCells[i], null);
            }
        }

        private IEnumerator BreakTilesThenClear(List<Vector3Int> selectedCells)
        {
            List<TileShard> shards = new List<TileShard>();
            for (int i = 0; i < selectedCells.Count; i++)
            {
                CreateShards(selectedCells[i], shards);
                tilemap.SetTile(selectedCells[i], null);
            }

            RefreshTilemapCollision();

            float elapsed = 0f;
            while (elapsed < breakDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / breakDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                for (int i = 0; i < shards.Count; i++)
                {
                    TileShard shard = shards[i];
                    if (shard.Transform == null)
                    {
                        continue;
                    }

                    Vector3 arc = Vector3.up * (Mathf.Sin(t * Mathf.PI) * shardLift);
                    shard.Transform.position = Vector3.Lerp(shard.StartPosition, shard.EndPosition, eased) + arc;
                    shard.Transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(shard.StartRotation, shard.EndRotation, eased));
                    shard.Transform.localScale = Vector3.Lerp(shard.StartScale, Vector3.zero, t * t);
                }

                yield return null;
            }

            for (int i = 0; i < shards.Count; i++)
            {
                if (shards[i].Transform != null)
                {
                    Destroy(shards[i].Transform.gameObject);
                }
            }

            breakRoutine = null;
        }

        private void CreateShards(Vector3Int cell, List<TileShard> shards)
        {
            Sprite sprite = tilemap.GetSprite(cell);
            if (sprite == null)
            {
                return;
            }

            Vector3 center = tilemap.GetCellCenterWorld(cell);
            Vector3 cellSize = tilemap.layoutGrid != null ? tilemap.layoutGrid.cellSize : Vector3.one;
            Vector3 spriteSize = sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
            {
                return;
            }

            Color color = tilemap.GetColor(cell);
            TilemapRenderer tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
            int sortingOrder = tilemapRenderer != null ? tilemapRenderer.sortingOrder + 2 : 2;
            Vector3 baseScale = new Vector3(cellSize.x / spriteSize.x, cellSize.y / spriteSize.y, 1f) * Mathf.Max(0.01f, shardScale);
            Vector2[] offsets =
            {
                new Vector2(-0.22f, -0.16f),
                new Vector2(0.22f, -0.13f),
                new Vector2(-0.18f, 0.18f),
                new Vector2(0.2f, 0.2f)
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject shardObject = new GameObject("TileShard");
                shardObject.transform.SetParent(transform, true);
                Vector3 start = center + new Vector3(offsets[i].x * cellSize.x, offsets[i].y * cellSize.y, 0f);
                Vector3 direction = (new Vector3(offsets[i].x, offsets[i].y + 0.15f, 0f)).normalized;
                if (direction.sqrMagnitude < 0.01f)
                {
                    direction = Vector3.up;
                }

                shardObject.transform.position = start;
                shardObject.transform.localScale = baseScale;

                SpriteRenderer renderer = shardObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = color;
                renderer.sortingOrder = sortingOrder;

                shards.Add(new TileShard(
                    shardObject.transform,
                    start,
                    start + direction * shardDistance,
                    0f,
                    (i % 2 == 0 ? -1f : 1f) * UnityEngine.Random.Range(120f, 240f),
                    baseScale));
            }
        }

        [ContextMenu("记录当前墙块")]
        public void CacheTiles()
        {
            originalTiles.Clear();
            if (tilemap == null)
            {
                return;
            }

            foreach (Vector3Int cell in EnumerateUniqueCells())
            {
                TileBase tile = tilemap.GetTile(cell);
                if (tile == null)
                {
                    continue;
                }

                originalTiles[cell] = new TileState(
                    tile,
                    tilemap.GetTransformMatrix(cell),
                    tilemap.GetColor(cell));
            }
        }

        private IEnumerable<Vector3Int> EnumerateUniqueCells()
        {
            HashSet<Vector3Int> uniqueCells = new HashSet<Vector3Int>();
            if (selectionMode == TileSelectionMode.WholeTilemap)
            {
                if (tilemap == null)
                {
                    yield break;
                }

                BoundsInt tilemapBounds = tilemap.cellBounds;
                foreach (Vector3Int cell in tilemapBounds.allPositionsWithin)
                {
                    if (tilemap.GetTile(cell) != null && uniqueCells.Add(cell))
                    {
                        yield return cell;
                    }
                }

                yield break;
            }

            if (selectionMode == TileSelectionMode.ManualCells && cells != null)
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    if (uniqueCells.Add(cells[i]))
                    {
                        yield return cells[i];
                    }
                }
            }

            if (selectionMode == TileSelectionMode.RectAreas)
            {
                foreach (Vector3Int areaCell in EnumerateAreaCells(uniqueCells))
                {
                    yield return areaCell;
                }

                yield break;
            }

            if (selectionMode != TileSelectionMode.ButtonWallChannel || tilemap == null)
            {
                yield break;
            }

            BoundsInt channelBounds = tilemap.cellBounds;
            foreach (Vector3Int cell in channelBounds.allPositionsWithin)
            {
                TileBase tile = tilemap.GetTile(cell);
                ButtonWallTile buttonWallTile = tile as ButtonWallTile;
                if (buttonWallTile != null && buttonWallTile.Channel == wallChannel && uniqueCells.Add(cell))
                {
                    yield return cell;
                }
            }
        }

        private IEnumerable<Vector3Int> EnumerateAreaCells(HashSet<Vector3Int> uniqueCells)
        {
            if (areas == null)
            {
                yield break;
            }

            for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
            {
                TileRect area = areas[areaIndex];
                int width = Mathf.Max(0, area.width);
                int height = Mathf.Max(0, area.height);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Vector3Int cell = new Vector3Int(area.origin.x + x, area.origin.y + y, area.origin.z);
                        if (uniqueCells.Add(cell))
                        {
                            yield return cell;
                        }
                    }
                }
            }
        }

        private void RefreshTilemapCollision()
        {
            tilemap.RefreshAllTiles();
            TilemapCollider2D tilemapCollider = tilemap.GetComponent<TilemapCollider2D>();
            if (tilemapCollider != null)
            {
                tilemapCollider.ProcessTilemapChanges();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (tilemap == null)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.35f);
            foreach (Vector3Int cell in EnumerateUniqueCells())
            {
                Vector3 center = tilemap.GetCellCenterWorld(cell);
                Vector3 size = tilemap.layoutGrid != null ? tilemap.layoutGrid.cellSize : Vector3.one;
                Gizmos.DrawCube(center, size);
            }
        }

        private readonly struct TileState
        {
            public TileState(TileBase tile, Matrix4x4 transform, Color color)
            {
                Tile = tile;
                Transform = transform;
                Color = color;
            }

            public TileBase Tile { get; }
            public Matrix4x4 Transform { get; }
            public Color Color { get; }
        }

        private readonly struct TileShard
        {
            public TileShard(
                Transform transform,
                Vector3 startPosition,
                Vector3 endPosition,
                float startRotation,
                float endRotation,
                Vector3 startScale)
            {
                Transform = transform;
                StartPosition = startPosition;
                EndPosition = endPosition;
                StartRotation = startRotation;
                EndRotation = endRotation;
                StartScale = startScale;
            }

            public Transform Transform { get; }
            public Vector3 StartPosition { get; }
            public Vector3 EndPosition { get; }
            public float StartRotation { get; }
            public float EndRotation { get; }
            public Vector3 StartScale { get; }
        }
    }
}
