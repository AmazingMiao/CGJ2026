using System;
using System.Collections.Generic;
using System.IO;
using CGJ2026.Boulder;
using CGJ2026.Gameplay;
using CGJ2026.Input;
using CGJ2026.Player;
using CGJ2026.View;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace CGJ2026.EditorTools
{
    public static class PrototypeSceneBuilder
    {
        private const string MapPath = "Assets/Scenes/Map.unity";
        private const string InputActionsPath = "Assets/Game/Input/CGJInputActions.inputactions";
        private const string GroundTilePath = "Assets/Game/Tiles/Ground/Box.asset";
        private const string LegacyGroundTilePath = "Assets/Game/Tiles/PlaceholderGroundTile.asset";
        private const string SlopeTileRoot = "Assets/Game/Tiles/Slope";
        private const string TilePalettePath = "Assets/Game/TilePalettes/WhiteboxPalette.prefab";
        private const string TerrainArtRoot = "Assets/Game/Art/Placeholders/Terrain";
        private const string SlopeArtRoot = TerrainArtRoot + "/Slope";
        private const string GroundSpritePath = TerrainArtRoot + "/box.png";
        private const string PlayerPhysicsMaterialPath = "Assets/Game/Physics/PlayerNoFriction.physicsMaterial2d";
        private const string RainWaterPhysicsMaterialPath = "Assets/Game/Physics/RainWater.physicsMaterial2D";
        private const string PrefabsRoot = "Assets/Game/Prefabs";
        private const string PlayerPrefabPath = PrefabsRoot + "/Player.prefab";
        private const string BoulderPrefabPath = PrefabsRoot + "/Boulder.prefab";
        private const string PlayerDeathVfxPrefabPath = PrefabsRoot + "/PlayerDeathExplosion.prefab";
        private const string PlayerDeathVfxSpritePath = "Assets/Game/Art/Placeholders/placeholder_gib.png";
        private const string PlayerDeathVfxMaterialPath = "Assets/Game/Art/Placeholders/PlayerDeathExplosionMaterial.mat";
        private const string PlayerCapsuleSpritePath = "Assets/Game/Art/Placeholders/placeholder_player_capsule.png";
        private const string AnimationRoot = "Assets/Game/Animation";
        private const string PlayerControllerPath = AnimationRoot + "/PlayerPlaceholder.controller";
        private const string PlayerIdleClipPath = AnimationRoot + "/Player_Idle_Placeholder.anim";
        private const string PlayerDeathClipPath = AnimationRoot + "/Player_Death_Placeholder.anim";
        private static readonly int[] SupportedSlopeAngles = { 30, 45, 60 };
        private static readonly Vector2Int[] SupportedWideSlopeSizes =
        {
            new Vector2Int(3, 1),
            new Vector2Int(3, 2),
            new Vector2Int(2, 1)
        };

        private static readonly Color32 PlayerColor = new Color32(30, 120, 255, 255);
        private static readonly Color32 PlayerCapsuleColor = new Color32(30, 120, 255, 255);
        private static readonly Color32 BoulderFill = new Color32(70, 70, 70, 255);
        private static readonly Color32 BoulderLine = new Color32(20, 20, 20, 255);
        private static readonly Color32 GroundFill = new Color32(240, 240, 240, 255);

        [MenuItem("CGJ2026/Build Prototype Map (Overwrite)")]
        public static void BuildPrototypeScene()
        {
            BuildAllWhiteboxLevels();
        }

        [MenuItem("CGJ2026/Build Prototype Map From Script (Overwrite)")]
        public static void BuildAllWhiteboxLevels()
        {
            Directory.CreateDirectory("Assets/Game/Art/Placeholders");
            Directory.CreateDirectory(TerrainArtRoot);
            Directory.CreateDirectory(SlopeArtRoot);
            Directory.CreateDirectory("Assets/Game/Tiles/Ground");
            Directory.CreateDirectory(SlopeTileRoot);
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Game/Physics");
            Directory.CreateDirectory(PrefabsRoot);

            int worldLayer = EnsureLayer("World");
            Sprite playerSprite = CreateSolidSprite("Assets/Game/Art/Placeholders/placeholder_player.png", PlayerColor);
            Sprite playerCapsuleSprite = CreateCapsuleSprite(PlayerCapsuleSpritePath, PlayerCapsuleColor);
            Sprite groundSprite = CreateSolidSprite(GroundSpritePath, GroundFill);
            Sprite boulderSprite = CreateCircleSprite("Assets/Game/Art/Placeholders/placeholder_boulder.png", BoulderFill, BoulderLine);
            Tile groundTile = CreateGroundTile(groundSprite);
            PhysicsMaterial2D playerMaterial = CreatePlayerPhysicsMaterial();
            PhysicsMaterial2D rainWaterMaterial = CreateRainWaterPhysicsMaterial();
            SlopeTileLibrary slopeTiles = CreateSlopeTileLibrary();
            CreateWhiteboxTilePalette(groundTile, slopeTiles);
            ParticleSystem deathVfxPrefab = CreatePlayerDeathVfxPrefab();

            BuildLevel(
                MapPath,
                "Map",
                worldLayer,
                playerSprite,
                playerCapsuleSprite,
                boulderSprite,
                groundTile,
                playerMaterial,
                rainWaterMaterial,
                deathVfxPrefab,
                cameraStart: new Vector3(-3f, 3.4f, -10f),
                cameraMaxX: 25f,
                playerStart: new Vector3(-5f, 1f, 0f),
                boulderStart: new Vector3(-10f, 1.5f, 0f),
                checkpointPosition: new Vector3(23f, 8.5f, 0f),
                paintLayout: (world) =>
                {
                    PaintRect(world, groundTile, groundTile.sprite, -12, -1, 11, 1);
                    PaintRect(world, groundTile, groundTile.sprite, 4, 4, 2, 1);
                    PaintRect(world, groundTile, groundTile.sprite, 6, 3, 3, 4);
                    PaintRect(world, groundTile, groundTile.sprite, -12, -5, 1, 15);
                    PaintRect(world, groundTile, groundTile.sprite, 25, -5, 1, 15);
                    PaintRect(world, groundTile, groundTile.sprite, -12, 9, 38, 1);
                    PaintSlope(world, slopeTiles, groundTile, groundTile.sprite, worldLayer, new Vector2(-1f, 0f), new Vector2(6f, 4f));
                    PaintSlope(world, slopeTiles, groundTile, groundTile.sprite, worldLayer, new Vector2(9f, 3f), new Vector2(25f, 8f));
                },
                buildExtras: null);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MapPath, true)
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Built CGJ2026 Map. Generated slope tiles into a single Ground tilemap, and (re)saved Player/Boulder as connected prefabs.");
        }

        [MenuItem("CGJ2026/Install Capsule Player Visual In Open Scene")]
        public static void InstallCapsulePlayerVisualInOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Stop Play Mode before installing the capsule player visual.");
                return;
            }

            PlayerController playerController = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (playerController == null)
            {
                throw new InvalidOperationException("The open scene must contain a PlayerController.");
            }

            Sprite capsuleSprite = CreateCapsuleSprite(PlayerCapsuleSpritePath, PlayerCapsuleColor);
            GameObject player = playerController.gameObject;

            Transform oldVisual = player.transform.Find("Sprite");
            if (oldVisual != null)
            {
                UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);
            }

            CapsuleVisualReferences visual = CreateCapsulePlayerVisual(player, capsuleSprite);

            RespawnService respawnService = UnityEngine.Object.FindFirstObjectByType<RespawnService>();
            if (respawnService != null)
            {
                PlayerDeathVfx deathVfx = respawnService.GetComponent<PlayerDeathVfx>();
                if (deathVfx != null)
                {
                    SetObjectRef(deathVfx, "playerRenderer", visual.Renderer);
                    SetObjectRef(deathVfx, "playerVisualRoot", visual.Root);
                }
            }

            EditorUtility.SetDirty(player);
            UnityEngine.SceneManagement.Scene scene = player.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Replaced the player biped IK and Animator hierarchy with a single capsule visual.");
        }

        [MenuItem("CGJ2026/Rebuild Whitebox Tile Palette")]
        public static void RebuildWhiteboxTilePalette()
        {
            Directory.CreateDirectory("Assets/Game/Art/Placeholders");
            Directory.CreateDirectory(TerrainArtRoot);
            Directory.CreateDirectory(SlopeArtRoot);
            Directory.CreateDirectory("Assets/Game/Tiles/Ground");
            Directory.CreateDirectory(SlopeTileRoot);

            Sprite groundSprite = CreateSolidSprite(GroundSpritePath, GroundFill);
            Tile groundTile = CreateGroundTile(groundSprite);
            SlopeTileLibrary slopeTiles = CreateSlopeTileLibrary();
            CreateWhiteboxTilePalette(groundTile, slopeTiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Rebuilt whitebox tile palette: " + TilePalettePath);
        }

        [MenuItem("CGJ2026/Open Whitebox Tilemap Workspace")]
        public static void OpenWhiteboxTilemapWorkspace()
        {
            GameObject palette = AssetDatabase.LoadAssetAtPath<GameObject>(TilePalettePath);
            if (palette == null)
            {
                Debug.LogWarning("Whitebox tile palette is missing. Run CGJ2026/Build Map once to regenerate it.");
                return;
            }

            EditorApplication.ExecuteMenuItem("Window/2D/Tile Palette");
            EditorApplication.ExecuteMenuItem("Window/General/Scene");
            TrySetActiveTilePalette(palette);
            SelectTilemapForPainting();
            EditorGUIUtility.PingObject(palette);
            Debug.Log("Opened whitebox tilemap workspace. Palette: " + TilePalettePath);
        }

        [MenuItem("CGJ2026/Clear Ground Tiles In Map")]
        public static void ClearGroundTilesInLevels()
        {
            ClearGroundTiles(MapPath);
            Debug.Log("Cleared Ground tiles/blocks from Map. Player, Boulder, checkpoint, camera, and kill zone are untouched.");
        }

        private static void ClearGroundTiles(string scenePath)
        {
            if (!File.Exists(scenePath))
            {
                Debug.LogWarning("Skipped missing scene while clearing ground tiles: " + scenePath);
                return;
            }

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Tilemap ground = FindSceneTilemap("Ground");
            if (ground != null)
            {
                ground.ClearAllTiles();
                ground.CompressBounds();
                ground.RefreshAllTiles();
                SyncTilemapCollision(ground);
                EditorUtility.SetDirty(ground);
                EditorUtility.SetDirty(ground.gameObject);
            }

            DeleteGeneratedSceneObject("GroundView");
            DeleteGeneratedSceneObject("SlopeColliders");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void DeleteGeneratedSceneObject(string objectName)
        {
            GameObject generatedObject = GameObject.Find(objectName);
            if (generatedObject == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(generatedObject);
        }

        private static void BuildLevel(
            string scenePath,
            string sceneName,
            int worldLayer,
            Sprite playerSprite,
            Sprite playerCapsuleSprite,
            Sprite boulderSprite,
            Tile groundTile,
            PhysicsMaterial2D playerMaterial,
            PhysicsMaterial2D rainWaterMaterial,
            ParticleSystem deathVfxPrefab,
            Vector3 cameraStart,
            float cameraMaxX,
            Vector3 playerStart,
            Vector3 boulderStart,
            Vector3 checkpointPosition,
            Action<LevelTilemaps> paintLayout,
            Action<GameObject> buildExtras)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = sceneName;

            GameObject root = new GameObject(sceneName);
            GameInputReader inputReader = CreateInputReader(root.transform);
            CameraFollow2D cameraFollow = CreateCamera(root.transform, cameraStart, cameraMaxX);
            CreateLight(root.transform);
            LevelTilemaps world = CreateTilemapWorld(root.transform, worldLayer);
            paintLayout(world);
            FinalizeTilemap(world.Ground);
            world.Ground.CompressBounds();
            FinalizeTilemap(world.Ground);
            EditorSceneManager.MarkSceneDirty(scene);

            Rigidbody2D playerBody = CreatePlayer(root.transform, inputReader, playerCapsuleSprite, worldLayer, playerStart, playerMaterial);
            Rigidbody2D boulderBody = CreateBoulder(root.transform, inputReader, boulderSprite, worldLayer, boulderStart, cameraFollow);
            CreateRainEmitter(root.transform, rainWaterMaterial, playerSprite, playerStart + Vector3.up * 5.6f);
            SetObjectRef(cameraFollow, "target", playerBody.transform);

            RespawnService respawnService = CreateRespawnService(root.transform, playerBody, boulderBody, deathVfxPrefab);
            SetObjectRef(playerBody.GetComponent<PlayerImpactHandler>(), "respawnService", respawnService);
            CreateCheckpoint(root.transform, playerSprite, respawnService, checkpointPosition);
            CreateKillZone(root.transform, respawnService, new Vector3(6f, -8f, 0f), new Vector2(80f, 2f));
            buildExtras?.Invoke(root);

            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static GameInputReader CreateInputReader(Transform parent)
        {
            GameObject systems = new GameObject("Input");
            systems.transform.SetParent(parent, false);

            GameInputReader inputReader = systems.AddComponent<GameInputReader>();
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
            {
                throw new InvalidOperationException("Missing input actions at " + InputActionsPath);
            }

            inputReader.AssignInputActions(actions);
            EditorUtility.SetDirty(inputReader);
            return inputReader;
        }

        private static void CreateRainEmitter(
            Transform parent,
            PhysicsMaterial2D waterMaterial,
            Sprite nozzleSprite,
            Vector3 position)
        {
            GameObject emitterObject = new GameObject("RainEmitter");
            emitterObject.transform.SetParent(parent, false);
            emitterObject.transform.position = position;
            RainEmitter2D emitter = emitterObject.AddComponent<RainEmitter2D>();
            SetObjectRef(emitter, "waterMaterial", waterMaterial);

            GameObject nozzle = new GameObject("Nozzle");
            nozzle.transform.SetParent(emitterObject.transform, false);
            nozzle.transform.localScale = new Vector3(2.1f, 0.28f, 1f);
            SpriteRenderer nozzleRenderer = nozzle.AddComponent<SpriteRenderer>();
            nozzleRenderer.sprite = nozzleSprite;
            nozzleRenderer.color = new Color(0.12f, 0.22f, 0.3f, 1f);
            nozzleRenderer.sortingOrder = 19;

            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer >= 0)
            {
                Physics2D.IgnoreLayerCollision(waterLayer, waterLayer, true);
            }
        }

        private static CameraFollow2D CreateCamera(Transform parent, Vector3 startPosition, float maxX)
        {
            GameObject cameraObject = new GameObject("Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = startPosition;

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.aspect = 16f / 9f;
            camera.orthographicSize = 9f;
            camera.backgroundColor = new Color(0.04f, 0.04f, 0.05f);
            camera.clearFlags = CameraClearFlags.SolidColor;

            CameraFollow2D follow = cameraObject.AddComponent<CameraFollow2D>();
            SetFloat(follow, "maxX", maxX);
            return follow;
        }

        private static void CreateLight(Transform parent)
        {
            Type light2DType = Type.GetType("UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (light2DType == null)
            {
                return;
            }

            GameObject lightObject = new GameObject("Light");
            lightObject.transform.SetParent(parent, false);
            Component light = lightObject.AddComponent(light2DType);
            SerializedObject lightSerialized = new SerializedObject(light);

            SerializedProperty lightType = lightSerialized.FindProperty("m_LightType");
            if (lightType != null)
            {
                lightType.enumValueIndex = 5;
            }

            SerializedProperty intensity = lightSerialized.FindProperty("m_Intensity");
            if (intensity != null)
            {
                intensity.floatValue = 1f;
            }

            lightSerialized.ApplyModifiedProperties();
        }

        private static LevelTilemaps CreateTilemapWorld(Transform parent, int worldLayer)
        {
            GameObject gridObject = new GameObject("Grid");
            gridObject.transform.SetParent(parent, false);
            Grid grid = gridObject.AddComponent<Grid>();
            grid.cellSize = Vector3.one;

            GameObject groundObject = new GameObject("Ground");
            groundObject.layer = worldLayer;
            groundObject.transform.SetParent(gridObject.transform, false);

            Tilemap ground = groundObject.AddComponent<Tilemap>();
            groundObject.AddComponent<TilemapRenderer>();

            TilemapCollider2D tilemapCollider = groundObject.AddComponent<TilemapCollider2D>();

            Rigidbody2D worldBody = groundObject.AddComponent<Rigidbody2D>();
            worldBody.bodyType = RigidbodyType2D.Static;

            CompositeCollider2D compositeCollider = groundObject.AddComponent<CompositeCollider2D>();
            compositeCollider.geometryType = CompositeCollider2D.GeometryType.Outlines;
            tilemapCollider.compositeOperation = Collider2D.CompositeOperation.Merge;

            return new LevelTilemaps(ground);
        }

        private static Rigidbody2D CreatePlayer(
            Transform parent,
            GameInputReader inputReader,
            Sprite playerCapsuleSprite,
            int worldLayer,
            Vector3 position,
            PhysicsMaterial2D playerMaterial)
        {
            GameObject player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            player.transform.position = position;

            Rigidbody2D playerBody = player.AddComponent<Rigidbody2D>();
            playerBody.mass = 1.5f;
            playerBody.freezeRotation = true;
            playerBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            playerBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CapsuleCollider2D playerCollider = player.AddComponent<CapsuleCollider2D>();
            playerCollider.size = new Vector2(1f, 2f);
            playerCollider.direction = CapsuleDirection2D.Vertical;
            playerCollider.sharedMaterial = playerMaterial;

            GroundRaySensor2D groundSensor = player.AddComponent<GroundRaySensor2D>();
            groundSensor.Configure(new Vector2(0f, -0.82f), Vector2.down, 0.55f, 1 << worldLayer);

            CharacterMotor2D motor = player.AddComponent<CharacterMotor2D>();
            PlayerController playerController = player.AddComponent<PlayerController>();
            PlayerImpactHandler impactHandler = player.AddComponent<PlayerImpactHandler>();
            CreateCapsulePlayerVisual(player, playerCapsuleSprite);

            SetObjectRef(motor, "body", playerBody);
            SetObjectRef(motor, "groundSensor", groundSensor);
            SetObjectRef(playerController, "inputReader", inputReader);
            SetObjectRef(playerController, "motor", motor);
            SetObjectRef(impactHandler, "body", playerBody);
            SetObjectRef(impactHandler, "bodyCollider", playerCollider);
            SetLayerMask(impactHandler, "terrainMask", 1 << worldLayer);

            GameObject connected = PrefabUtility.SaveAsPrefabAssetAndConnect(player, PlayerPrefabPath, InteractionMode.AutomatedAction);
            return connected.GetComponent<Rigidbody2D>();
        }

        private static CapsuleVisualReferences CreateCapsulePlayerVisual(GameObject player, Sprite capsuleSprite)
        {
            GameObject visualObject = new GameObject("Sprite");
            visualObject.transform.SetParent(player.transform, false);
            SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();
            renderer.sprite = capsuleSprite;
            renderer.sortingOrder = 14;
            return new CapsuleVisualReferences(visualObject.transform, renderer);
        }

        private static Rigidbody2D CreateBoulder(
            Transform parent,
            GameInputReader inputReader,
            Sprite boulderSprite,
            int worldLayer,
            Vector3 position,
            CameraFollow2D cameraFollow)
        {
            GameObject boulder = new GameObject("Boulder");
            boulder.transform.SetParent(parent, false);
            boulder.transform.position = position;

            Rigidbody2D boulderBody = boulder.AddComponent<Rigidbody2D>();
            boulderBody.mass = 8f;
            boulderBody.angularDamping = 0.2f;
            boulderBody.linearDamping = 0.15f;
            boulderBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D boulderCollider = boulder.AddComponent<CircleCollider2D>();
            boulderCollider.radius = 1.5f;

            GroundRaySensor2D groundSensor = boulder.AddComponent<GroundRaySensor2D>();
            groundSensor.Configure(new Vector2(0f, -1.28f), Vector2.down, 0.7f, 1 << worldLayer);

            GameObject boulderVisual = new GameObject("Sprite");
            boulderVisual.transform.SetParent(boulder.transform, false);
            boulderVisual.transform.localScale = new Vector3(3f, 3f, 1f);

            SpriteRenderer boulderRenderer = boulderVisual.AddComponent<SpriteRenderer>();
            boulderRenderer.sprite = boulderSprite;
            boulderRenderer.sortingOrder = 9;
            BoulderAnimatorBridge animatorBridge = boulderVisual.AddComponent<BoulderAnimatorBridge>();

            BoulderGravityController boulderController = boulder.AddComponent<BoulderGravityController>();
            SetObjectRef(boulderController, "inputReader", inputReader);
            SetObjectRef(boulderController, "body", boulderBody);
            SetObjectRef(boulderController, "groundSensor", groundSensor);
            SetObjectRef(animatorBridge, "body", boulderBody);

            CollisionShakeEmitter shakeEmitter = boulder.AddComponent<CollisionShakeEmitter>();
            SetObjectRef(shakeEmitter, "cameraFollow", cameraFollow);
            SetLayerMask(shakeEmitter, "impactMask", 1 << worldLayer);

            GameObject connected = PrefabUtility.SaveAsPrefabAssetAndConnect(boulder, BoulderPrefabPath, InteractionMode.AutomatedAction);
            return connected.GetComponent<Rigidbody2D>();
        }

        private static RespawnService CreateRespawnService(Transform parent, Rigidbody2D playerBody, Rigidbody2D boulderBody, ParticleSystem deathVfxPrefab)
        {
            GameObject respawnObject = new GameObject("Respawn");
            respawnObject.transform.SetParent(parent, false);
            RespawnService respawnService = respawnObject.AddComponent<RespawnService>();
            SetObjectRef(respawnService, "playerBody", playerBody);
            SetObjectRef(respawnService, "boulderBody", boulderBody);

            PlayerDeathVfx deathVfx = respawnObject.AddComponent<PlayerDeathVfx>();
            SetObjectRef(deathVfx, "respawnService", respawnService);
            SetObjectRef(deathVfx, "explosionPrefab", deathVfxPrefab);
            SetLayerMask(deathVfx, "terrainMask", 1 << LayerMask.NameToLayer("World"));

            return respawnService;
        }

        private static void CreateCheckpoint(Transform parent, Sprite playerSprite, RespawnService respawnService, Vector3 position)
        {
            GameObject checkpoint = new GameObject("Checkpoint");
            checkpoint.transform.SetParent(parent, false);
            checkpoint.transform.position = position;
            checkpoint.transform.localScale = new Vector3(2f, 2f, 1f);

            SpriteRenderer checkpointRenderer = checkpoint.AddComponent<SpriteRenderer>();
            checkpointRenderer.sprite = playerSprite;
            checkpointRenderer.color = new Color(0.2f, 1f, 0.35f, 1f);
            checkpointRenderer.sortingOrder = 7;

            BoxCollider2D checkpointTrigger = checkpoint.AddComponent<BoxCollider2D>();
            checkpointTrigger.isTrigger = true;
            checkpointTrigger.size = new Vector2(3f, 2.2f);

            CheckpointZone checkpointZone = checkpoint.AddComponent<CheckpointZone>();
            SetObjectRef(checkpointZone, "respawnService", respawnService);
        }

        private static void CreateKillZone(Transform parent, RespawnService respawnService, Vector3 position, Vector2 size)
        {
            GameObject fallZone = new GameObject("KillZone");
            fallZone.transform.SetParent(parent, false);
            fallZone.transform.position = position;

            BoxCollider2D trigger = fallZone.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = size;

            FallRespawnZone fallRespawnZone = fallZone.AddComponent<FallRespawnZone>();
            SetObjectRef(fallRespawnZone, "respawnService", respawnService);
        }

        private static void PaintRect(
            LevelTilemaps world,
            Tile groundTile,
            Sprite groundSprite,
            int minX,
            int minY,
            int width,
            int height)
        {
            BoundsInt bounds = new BoundsInt(minX, minY, 0, width, height, 1);
            TileBase[] tiles = new TileBase[width * height];
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = groundTile;
            }

            world.Ground.SetTilesBlock(bounds, tiles);

            CreateGroundViewBlock(world.GroundView, groundSprite, minX, minY, width, height);
        }

        private static void FinalizeTilemap(Tilemap tilemap)
        {
            tilemap.RefreshAllTiles();
            EditorUtility.SetDirty(tilemap);
            EditorUtility.SetDirty(tilemap.gameObject);
        }

        private static void PaintSlope(
            LevelTilemaps world,
            SlopeTileLibrary slopeTiles,
            Tile groundTile,
            Sprite groundSprite,
            int worldLayer,
            Vector2 start,
            Vector2 end)
        {
            float run = Mathf.Abs(end.x - start.x);
            float rise = Mathf.Abs(end.y - start.y);
            if (run <= 0.01f || rise <= 0.01f)
            {
                return;
            }

            int tileAngle = ToSupportedSlopeAngle(Mathf.Atan2(rise, run) * Mathf.Rad2Deg);
            bool risesRight = end.x > start.x ? end.y > start.y : start.y > end.y;
            Tile slopeTile = slopeTiles.Get(tileAngle, risesRight);

            int minX = Mathf.FloorToInt(Mathf.Min(start.x, end.x));
            int maxX = Mathf.CeilToInt(Mathf.Max(start.x, end.x));
            int baseY = Mathf.FloorToInt(Mathf.Min(start.y, end.y));
            for (int x = minX; x < maxX; x++)
            {
                float leftT = Mathf.InverseLerp(start.x, end.x, x);
                float rightT = Mathf.InverseLerp(start.x, end.x, x + 1f);
                float leftY = Mathf.Lerp(start.y, end.y, leftT);
                float rightY = Mathf.Lerp(start.y, end.y, rightT);
                int topY = Mathf.FloorToInt(Mathf.Max(leftY, rightY));

                for (int y = baseY; y < topY && groundTile != null; y++)
                {
                    world.Ground.SetTile(new Vector3Int(x, y, 0), groundTile);
                }

                world.Ground.SetTile(new Vector3Int(x, topY, 0), slopeTile);
            }
        }

        private static void PaintSlopeByAngle(
            LevelTilemaps world,
            SlopeTileLibrary slopeTiles,
            Tile groundTile,
            Sprite groundSprite,
            int worldLayer,
            Vector2 start,
            float horizontalRun,
            int angleDegrees,
            bool risesRight)
        {
            int supportedAngle = ToSupportedSlopeAngle(angleDegrees);
            float direction = risesRight ? 1f : -1f;
            float rise = Mathf.Tan(supportedAngle * Mathf.Deg2Rad) * Mathf.Abs(horizontalRun);
            Vector2 end = start + new Vector2(direction * Mathf.Abs(horizontalRun), rise);
            PaintSlope(world, slopeTiles, groundTile, groundSprite, worldLayer, start, end);
        }

        private static int ToSupportedSlopeAngle(float angleDegrees)
        {
            int nearest = SupportedSlopeAngles[0];
            float bestDiff = Mathf.Abs(angleDegrees - nearest);
            for (int i = 1; i < SupportedSlopeAngles.Length; i++)
            {
                float diff = Mathf.Abs(angleDegrees - SupportedSlopeAngles[i]);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    nearest = SupportedSlopeAngles[i];
                }
            }

            return nearest;
        }

        private static void CreateGroundViewBlock(Transform parent, Sprite sprite, int minX, int minY, int width, int height)
        {
            if (parent == null)
            {
                return;
            }

            GameObject block = new GameObject($"Block_{minX}_{minY}_{width}x{height}");
            block.transform.SetParent(parent, false);
            block.transform.position = new Vector3(minX + width * 0.5f, minY + height * 0.5f, 0.02f);
            block.transform.localScale = new Vector3(width, height, 1f);

            SpriteRenderer renderer = block.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = -1;
        }

        private static void SyncTilemapCollision(Tilemap tilemap)
        {
            if (tilemap == null)
            {
                return;
            }

            TilemapCollider2D tilemapCollider = tilemap.GetComponent<TilemapCollider2D>();
            if (tilemapCollider != null)
            {
                tilemapCollider.ProcessTilemapChanges();
                EditorUtility.SetDirty(tilemapCollider);
            }

            CompositeCollider2D compositeCollider = tilemap.GetComponent<CompositeCollider2D>();
            if (compositeCollider != null)
            {
                compositeCollider.GenerateGeometry();
                EditorUtility.SetDirty(compositeCollider);
            }

            Physics2D.SyncTransforms();
        }

        private static SlopeTileLibrary CreateSlopeTileLibrary()
        {
            DeleteObsoleteSlopeAssets();
            Directory.CreateDirectory(SlopeTileRoot + "/UpRight");
            Directory.CreateDirectory(SlopeTileRoot + "/UpLeft");
            Directory.CreateDirectory(SlopeArtRoot + "/UpRight");
            Directory.CreateDirectory(SlopeArtRoot + "/UpLeft");

            SlopeTileLibrary library = new SlopeTileLibrary();
            foreach (int angle in SupportedSlopeAngles)
            {
                library.Set(angle, true, CreateSlopeTile(angle, true));
                library.Set(angle, false, CreateSlopeTile(angle, false));
            }

            foreach (Vector2Int size in SupportedWideSlopeSizes)
            {
                for (int segmentIndex = 0; segmentIndex < size.x; segmentIndex++)
                {
                    library.SetWide(size.x, size.y, segmentIndex, true, CreateWideSlopeTile(size.x, size.y, segmentIndex, true));
                    library.SetWide(size.x, size.y, segmentIndex, false, CreateWideSlopeTile(size.x, size.y, segmentIndex, false));
                }
            }

            return library;
        }

        private static void DeleteObsoleteSlopeAssets()
        {
            List<string> tileNames = new List<string>(BuildSlopeFileNames("Slope{0}_{1:00}.asset"));
            tileNames.AddRange(BuildWideSlopeFileNames("Slope{0}_{1}x{2}_{3}.asset"));
            DeleteGeneratedAssetsExcept(SlopeTileRoot, tileNames.ToArray());

            List<string> artNames = new List<string>(BuildSlopeFileNames("slope_{0}_{1:00}.png"));
            artNames.AddRange(BuildWideSlopeFileNames("slope_{0}_{1}x{2}_{3}.png"));
            DeleteGeneratedAssetsExcept(SlopeArtRoot, artNames.ToArray());
        }

        private static string[] BuildSlopeFileNames(string format)
        {
            string[] names = new string[SupportedSlopeAngles.Length * 2];
            int i = 0;
            foreach (int angle in SupportedSlopeAngles)
            {
                names[i++] = string.Format(format, "UpRight", angle);
                names[i++] = string.Format(format, "UpLeft", angle);
            }

            return names;
        }

        private static string[] BuildWideSlopeFileNames(string format)
        {
            List<string> names = new List<string>();
            foreach (Vector2Int size in SupportedWideSlopeSizes)
            {
                for (int segmentIndex = 0; segmentIndex < size.x; segmentIndex++)
                {
                    names.Add(string.Format(format, "UpRight", size.y, size.x, GetWideSlopeSegmentName(size.x, segmentIndex, true)));
                    names.Add(string.Format(format, "UpLeft", size.y, size.x, GetWideSlopeSegmentName(size.x, segmentIndex, false)));
                }
            }

            return names.ToArray();
        }

        private static void DeleteGeneratedAssetsExcept(string root, params string[] keptFileNames)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                if (Path.GetExtension(files[i]) == ".meta")
                {
                    continue;
                }

                string fileName = Path.GetFileName(files[i]);
                bool shouldKeep = Array.IndexOf(keptFileNames, fileName) >= 0;
                if (shouldKeep)
                {
                    continue;
                }

                string assetPath = files[i].Replace('\\', '/');
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static Tile CreateSlopeTile(int angleDegrees, bool risesRight)
        {
            string direction = risesRight ? "UpRight" : "UpLeft";
            string path = $"{SlopeTileRoot}/{direction}/Slope{direction}_{angleDegrees:00}.asset";
            Tile slopeTile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (slopeTile == null)
            {
                slopeTile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(slopeTile, path);
            }

            Sprite slopeSprite = CreateSolidSlopeSprite($"{SlopeArtRoot}/{direction}/slope_{direction}_{angleDegrees:00}.png", angleDegrees, risesRight, GroundFill);
            slopeTile.sprite = slopeSprite;
            slopeTile.color = Color.white;
            slopeTile.flags = TileFlags.None;
            slopeTile.colliderType = Tile.ColliderType.Sprite;
            slopeTile.transform = Matrix4x4.identity;
            EditorUtility.SetDirty(slopeTile);
            return slopeTile;
        }

        private static Tile CreateWideSlopeTile(int widthCells, int heightCells, int segmentIndex, bool risesRight)
        {
            string direction = risesRight ? "UpRight" : "UpLeft";
            string segmentName = GetWideSlopeSegmentName(widthCells, segmentIndex, risesRight);
            string path = $"{SlopeTileRoot}/{direction}/Slope{direction}_{heightCells}x{widthCells}_{segmentName}.asset";
            Tile slopeTile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (slopeTile == null)
            {
                slopeTile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(slopeTile, path);
            }

            Sprite slopeSprite = CreateSolidWideSlopeSprite(
                $"{SlopeArtRoot}/{direction}/slope_{direction}_{heightCells}x{widthCells}_{segmentName}.png",
                widthCells,
                heightCells,
                segmentIndex,
                risesRight,
                GroundFill);
            slopeTile.sprite = slopeSprite;
            slopeTile.color = Color.white;
            slopeTile.flags = TileFlags.None;
            slopeTile.colliderType = Tile.ColliderType.Sprite;
            slopeTile.transform = Matrix4x4.identity;
            EditorUtility.SetDirty(slopeTile);
            return slopeTile;
        }

        private static void CreateWhiteboxTilePalette(Tile groundTile, SlopeTileLibrary slopeTiles)
        {
            const string paletteFolder = "Assets/Game/TilePalettes";
            Directory.CreateDirectory(paletteFolder);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TilePalettePath) != null)
            {
                AssetDatabase.DeleteAsset(TilePalettePath);
            }

            GridPaletteUtility.CreateNewPalette(
                paletteFolder,
                "WhiteboxPalette",
                GridLayout.CellLayout.Rectangle,
                GridPalette.CellSizing.Manual,
                Vector3.one,
                GridLayout.CellSwizzle.XYZ);

            GameObject paletteRoot = PrefabUtility.LoadPrefabContents(TilePalettePath);
            Tilemap tilemap = paletteRoot.GetComponentInChildren<Tilemap>();
            tilemap.ClearAllTiles();
            SetPaletteTile(tilemap, new Vector3Int(0, 0, 0), groundTile);
            int paletteX = 1;
            foreach (int angle in SupportedSlopeAngles)
            {
                SetPaletteTile(tilemap, new Vector3Int(paletteX++, 0, 0), slopeTiles.Get(angle, true));
                SetPaletteTile(tilemap, new Vector3Int(paletteX++, 0, 0), slopeTiles.Get(angle, false));
            }

            int paletteY = -1;
            foreach (Vector2Int size in SupportedWideSlopeSizes)
            {
                int widePaletteX = 0;
                for (int segmentIndex = 0; segmentIndex < size.x; segmentIndex++)
                {
                    SetPaletteTile(tilemap, new Vector3Int(widePaletteX + segmentIndex, paletteY, 0), slopeTiles.GetWide(size.x, size.y, segmentIndex, true));
                }

                widePaletteX += size.x;
                for (int segmentIndex = 0; segmentIndex < size.x; segmentIndex++)
                {
                    SetPaletteTile(tilemap, new Vector3Int(widePaletteX + segmentIndex, paletteY, 0), slopeTiles.GetWide(size.x, size.y, segmentIndex, false));
                }

                paletteY -= size.y + 1;
            }

            tilemap.CompressBounds();
            PrefabUtility.SaveAsPrefabAsset(paletteRoot, TilePalettePath);
            PrefabUtility.UnloadPrefabContents(paletteRoot);
        }

        private static void SetPaletteTile(Tilemap tilemap, Vector3Int position, Tile tile)
        {
            tilemap.SetTile(position, tile);
            tilemap.SetTileFlags(position, TileFlags.None);
            tilemap.SetTransformMatrix(position, Matrix4x4.identity);
            tilemap.SetColor(position, Color.white);
        }

        private static void SelectTilemapForPainting()
        {
            Tilemap target = FindSceneTilemap("Ground");
            if (target == null)
            {
                return;
            }

            Selection.activeGameObject = target.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            TrySetScenePaintTarget(target.gameObject);
        }

        private static Tilemap FindSceneTilemap(string objectName)
        {
            Tilemap[] tilemaps = UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
            for (int i = 0; i < tilemaps.Length; i++)
            {
                if (tilemaps[i].name == objectName)
                {
                    return tilemaps[i];
                }
            }

            return null;
        }

        private static void TrySetActiveTilePalette(GameObject palette)
        {
            Type gridPaintingState = FindEditorType("UnityEditor.Tilemaps.GridPaintingState");
            PropertyInfo paletteProperty = gridPaintingState?.GetProperty("palette", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            paletteProperty?.SetValue(null, palette);
        }

        private static void TrySetScenePaintTarget(GameObject target)
        {
            Type gridPaintingState = FindEditorType("UnityEditor.Tilemaps.GridPaintingState");
            PropertyInfo targetProperty = gridPaintingState?.GetProperty("scenePaintTarget", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            targetProperty?.SetValue(null, target);
        }

        private static Type FindEditorType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static Tile CreateGroundTile(Sprite groundSprite)
        {
            if (AssetDatabase.LoadAssetAtPath<Tile>(LegacyGroundTilePath) != null)
            {
                AssetDatabase.DeleteAsset(LegacyGroundTilePath);
            }

            Tile groundTile = AssetDatabase.LoadAssetAtPath<Tile>(GroundTilePath);
            if (groundTile == null)
            {
                groundTile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(groundTile, GroundTilePath);
            }

            groundTile.sprite = groundSprite;
            groundTile.color = Color.white;
            groundTile.flags = TileFlags.None;
            groundTile.colliderType = Tile.ColliderType.Grid;
            EditorUtility.SetDirty(groundTile);
            return groundTile;
        }

        private static PhysicsMaterial2D CreatePlayerPhysicsMaterial()
        {
            Directory.CreateDirectory("Assets/Game/Physics");
            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(PlayerPhysicsMaterialPath);
            if (material == null)
            {
                material = new PhysicsMaterial2D("PlayerNoFriction");
                AssetDatabase.CreateAsset(material, PlayerPhysicsMaterialPath);
            }

            // Friction 0 on just one side of a contact is enough to zero it out (Unity combines
            // 2D friction as a geometric mean), which stops the player sticking to walls while falling.
            material.friction = 0f;
            material.bounciness = 0f;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static PhysicsMaterial2D CreateRainWaterPhysicsMaterial()
        {
            Directory.CreateDirectory("Assets/Game/Physics");
            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(RainWaterPhysicsMaterialPath);
            if (material == null)
            {
                material = new PhysicsMaterial2D("RainWater");
                AssetDatabase.CreateAsset(material, RainWaterPhysicsMaterialPath);
            }

            material.friction = 0f;
            material.bounciness = 0f;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static RuntimeAnimatorController CreatePlayerPlaceholderAnimator(Vector3 visualRestScale)
        {
            Directory.CreateDirectory(AnimationRoot);

            // Never rebuilt once it exists, so hand-authored states/transitions added later
            // (real walk/idle/jump clips) survive re-running the whitebox builder.
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
            if (existing != null)
            {
                return existing;
            }

            AnimationClip idleClip = CreatePlaceholderClip(PlayerIdleClipPath, "Idle_Placeholder", visualRestScale, squash: false);
            AnimationClip deathClip = CreatePlaceholderClip(PlayerDeathClipPath, "Death_Placeholder", visualRestScale, squash: true);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(PlayerControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("VerticalVelocity", AnimatorControllerParameterType.Float);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState idleState = stateMachine.AddState("Idle");
            idleState.motion = idleClip;
            stateMachine.defaultState = idleState;

            AnimatorState deathState = stateMachine.AddState("Death");
            deathState.motion = deathClip;

            AnimatorStateTransition toDeath = stateMachine.AddAnyStateTransition(deathState);
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Death");
            toDeath.hasExitTime = false;
            toDeath.duration = 0f;
            toDeath.canTransitionToSelf = false;

            AnimatorStateTransition toIdle = deathState.AddTransition(idleState);
            toIdle.hasExitTime = true;
            toIdle.exitTime = 1f;
            toIdle.hasFixedDuration = true;
            toIdle.duration = 0.05f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimationClip CreatePlaceholderClip(string assetPath, string clipName, Vector3 restScale, bool squash)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, assetPath);
            }

            clip.name = clipName;
            clip.frameRate = 30f;

            if (squash)
            {
                // Quick squash + shrink + fade "poof" so death is visibly distinct from a silent
                // teleport back to the checkpoint, without needing any real character art yet.
                AnimationCurve scaleX = new AnimationCurve(
                    new Keyframe(0f, restScale.x), new Keyframe(0.12f, restScale.x * 1.35f), new Keyframe(0.4f, 0f));
                AnimationCurve scaleY = new AnimationCurve(
                    new Keyframe(0f, restScale.y), new Keyframe(0.12f, restScale.y * 0.55f), new Keyframe(0.4f, 0f));
                AnimationCurve colorR = AnimationCurve.Linear(0f, 1f, 0.4f, 1f);
                AnimationCurve colorG = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.15f), new Keyframe(0.4f, 0.15f));
                AnimationCurve colorB = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.15f), new Keyframe(0.4f, 0.15f));
                AnimationCurve colorA = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.3f, 1f), new Keyframe(0.4f, 0f));

                clip.SetCurve(string.Empty, typeof(Transform), "localScale.x", scaleX);
                clip.SetCurve(string.Empty, typeof(Transform), "localScale.y", scaleY);
                clip.SetCurve(string.Empty, typeof(SpriteRenderer), "m_Color.r", colorR);
                clip.SetCurve(string.Empty, typeof(SpriteRenderer), "m_Color.g", colorG);
                clip.SetCurve(string.Empty, typeof(SpriteRenderer), "m_Color.b", colorB);
                clip.SetCurve(string.Empty, typeof(SpriteRenderer), "m_Color.a", colorA);

                AnimationClipSettings deathSettings = AnimationUtility.GetAnimationClipSettings(clip);
                deathSettings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(clip, deathSettings);
            }
            else
            {
                AnimationCurve holdScaleX = AnimationCurve.Constant(0f, 1f / 30f, restScale.x);
                AnimationCurve holdScaleY = AnimationCurve.Constant(0f, 1f / 30f, restScale.y);
                AnimationCurve holdColor = AnimationCurve.Constant(0f, 1f / 30f, 1f);

                clip.SetCurve(string.Empty, typeof(Transform), "localScale.x", holdScaleX);
                clip.SetCurve(string.Empty, typeof(Transform), "localScale.y", holdScaleY);
                clip.SetCurve(string.Empty, typeof(SpriteRenderer), "m_Color.r", holdColor);
                clip.SetCurve(string.Empty, typeof(SpriteRenderer), "m_Color.g", holdColor);
                clip.SetCurve(string.Empty, typeof(SpriteRenderer), "m_Color.b", holdColor);
                clip.SetCurve(string.Empty, typeof(SpriteRenderer), "m_Color.a", holdColor);

                AnimationClipSettings idleSettings = AnimationUtility.GetAnimationClipSettings(clip);
                idleSettings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, idleSettings);
            }

            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static Sprite CreateSolidSprite(string assetPath, Color32 color)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[32 * 32];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            ApplyFullRectSpriteMesh(importer);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Sprite CreateCapsuleSprite(string assetPath, Color32 color)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            const int width = 32;
            const int height = 64;
            const float radius = width * 0.5f;
            float centerX = (width - 1) * 0.5f;
            float bottomCenterY = radius - 0.5f;
            float topCenterY = height - radius - 0.5f;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            for (int y = 0; y < height; y++)
            {
                float nearestY = Mathf.Clamp(y, bottomCenterY, topCenterY);
                for (int x = 0; x < width; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, nearestY));
                    texture.SetPixel(x, y, distance <= radius ? color : Color.clear);
                }
            }

            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            ApplyFullRectSpriteMesh(importer);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Sprite CreateBoxSprite(string assetPath, Color32 fill, Color32 border)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    bool isBorder = x == 0 || y == 0 || x == 31 || y == 31;
                    texture.SetPixel(x, y, isBorder ? border : fill);
                }
            }

            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            ApplyFullRectSpriteMesh(importer);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Sprite CreateSolidSlopeSprite(string assetPath, int angleDegrees, bool risesRight, Color32 fill)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float slope = Mathf.Tan(angleDegrees * Mathf.Deg2Rad);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float normalizedX = (x + 0.5f) / size;
                    float normalizedY = (y + 0.5f) / size;
                    float surfaceY = risesRight
                        ? slope * normalizedX
                        : slope * (1f - normalizedX);
                    bool filled = normalizedY <= Mathf.Clamp01(surfaceY);
                    texture.SetPixel(x, y, filled ? fill : new Color32(0, 0, 0, 0));
                }
            }

            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            ApplyFullRectSpriteMesh(importer);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Sprite CreateSolidWideSlopeSprite(string assetPath, int widthCells, int heightCells, int segmentIndex, bool risesRight, Color32 fill)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            if (heightCells > 1 && AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            const int pixelsPerCell = 64;
            int textureHeight = pixelsPerCell * heightCells;
            Texture2D texture = new Texture2D(pixelsPerCell, textureHeight, TextureFormat.RGBA32, false);

            for (int y = 0; y < textureHeight; y++)
            {
                for (int x = 0; x < pixelsPerCell; x++)
                {
                    float normalizedX = (x + 0.5f) / pixelsPerCell;
                    float localY = (y + 0.5f) / pixelsPerCell;
                    float surfaceY = GetWideSlopeSurfaceY(widthCells, heightCells, segmentIndex, normalizedX, risesRight);
                    bool filled = localY <= surfaceY;
                    texture.SetPixel(x, y, filled ? fill : new Color32(0, 0, 0, 0));
                }
            }

            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerCell;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(0.5f, 0.5f / heightCells);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static float GetWideSlopeSurfaceY(int widthCells, int heightCells, int segmentIndex, float normalizedX, bool risesRight)
        {
            float horizontalPosition = segmentIndex + normalizedX;
            return risesRight
                ? horizontalPosition * heightCells / widthCells
                : (widthCells - horizontalPosition) * heightCells / widthCells;
        }

        private static string GetWideSlopeSegmentName(int widthCells, int segmentIndex, bool risesRight)
        {
            int heightRank = risesRight ? segmentIndex : widthCells - segmentIndex - 1;
            if (heightRank == 0)
            {
                return "Lower";
            }

            if (heightRank == widthCells - 1)
            {
                return "Upper";
            }

            return "Middle";
        }

        private static void ApplyFullRectSpriteMesh(TextureImporter importer)
        {
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }

        private static Sprite CreateCircleSprite(string assetPath, Color32 fill, Color32 outline)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Vector2 center = new Vector2(31.5f, 31.5f);

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    Color32 pixel = new Color32(0, 0, 0, 0);
                    if (distance <= 29f)
                    {
                        pixel = distance > 25f ? outline : fill;
                    }

                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 64;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Broforce-style death feedback: a burst of solid-color gib squares that fly outward and
        // fall under gravity, then self-destroy once the burst finishes playing.
        private static ParticleSystem CreatePlayerDeathVfxPrefab()
        {
            Sprite gibSprite = CreateSolidSprite(PlayerDeathVfxSpritePath, new Color32(255, 255, 255, 255));

            Material material = AssetDatabase.LoadAssetAtPath<Material>(PlayerDeathVfxMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, PlayerDeathVfxMaterialPath);
            }

            material.mainTexture = gibSprite.texture;
            EditorUtility.SetDirty(material);

            GameObject vfxObject = new GameObject("PlayerDeathExplosion");
            ParticleSystem particles = vfxObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0f, 0f), new Color(1f, 0.25f, 0.1f));
            main.gravityModifier = 2.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.2f, 0.05f), 0f),
                    new GradientColorKey(new Color(0.4f, 0f, 0f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sharedMaterial = material;
            particleRenderer.sortingOrder = 20;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(vfxObject, PlayerDeathVfxPrefabPath);
            UnityEngine.Object.DestroyImmediate(vfxObject);

            return prefab.GetComponent<ParticleSystem>();
        }

        private sealed class LevelTilemaps
        {
            public LevelTilemaps(Tilemap ground)
            {
                Ground = ground;
            }

            public Tilemap Ground { get; }
            public Transform GroundView => null;
        }

        private sealed class CapsuleVisualReferences
        {
            public CapsuleVisualReferences(Transform root, SpriteRenderer renderer)
            {
                Root = root;
                Renderer = renderer;
            }

            public Transform Root { get; }
            public SpriteRenderer Renderer { get; }
        }

        private sealed class SlopeTileLibrary
        {
            private readonly Dictionary<(int angleDegrees, bool risesRight), Tile> tiles = new Dictionary<(int, bool), Tile>();
            private readonly Dictionary<(int widthCells, int heightCells, int segmentIndex, bool risesRight), Tile> wideTiles = new Dictionary<(int, int, int, bool), Tile>();

            public void Set(int angleDegrees, bool risesRight, Tile tile)
            {
                tiles[(angleDegrees, risesRight)] = tile;
            }

            public void SetWide(int widthCells, int heightCells, int segmentIndex, bool risesRight, Tile tile)
            {
                wideTiles[(widthCells, heightCells, segmentIndex, risesRight)] = tile;
            }

            public Tile Get(int angleDegrees, bool risesRight)
            {
                return tiles[(angleDegrees, risesRight)];
            }

            public Tile GetWide(int widthCells, int heightCells, int segmentIndex, bool risesRight)
            {
                return wideTiles[(widthCells, heightCells, segmentIndex, risesRight)];
            }
        }

        private static int EnsureLayer(string layerName)
        {
            UnityEngine.Object tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            SerializedObject tagManager = new SerializedObject(tagManagerAsset);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (layer.stringValue == layerName)
                {
                    return i;
                }
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layer.stringValue))
                {
                    layer.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }

            throw new InvalidOperationException("No free Unity layer slot for " + layerName);
        }

        private static void SetObjectRef(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property {propertyName} on {target.GetType().Name}");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }

        private static void SetLayerMask(UnityEngine.Object target, string propertyName, int value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property {propertyName} on {target.GetType().Name}");
            }

            property.intValue = value;
            serialized.ApplyModifiedProperties();
        }

        private static void SetFloat(UnityEngine.Object target, string propertyName, float value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property {propertyName} on {target.GetType().Name}");
            }

            property.floatValue = value;
            serialized.ApplyModifiedProperties();
        }

        private static void SetVector2(UnityEngine.Object target, string propertyName, Vector2 value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized property {propertyName} on {target.GetType().Name}");
            }

            property.vector2Value = value;
            serialized.ApplyModifiedProperties();
        }
    }
}
