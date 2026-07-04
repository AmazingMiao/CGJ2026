using System.IO;
using CGJ2026.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CGJ2026.EditorTools
{
    public static class SeesawPrefabBuilder
    {
        private const string PrefabsRoot = "Assets/Game/Prefabs";
        private const string ArtRoot = "Assets/Game/Art/Placeholders/Mechanics";
        private const string PhysicsRoot = "Assets/Game/Physics";
        private const string PrefabPath = PrefabsRoot + "/Seesaw_7x2.prefab";
        private const string BoardSpritePath = ArtRoot + "/seesaw_board.png";
        private const string FulcrumSpritePath = ArtRoot + "/seesaw_fulcrum.png";
        private const string PhysicsMaterialPath = PhysicsRoot + "/SeesawLowFriction.physicsMaterial2d";

        private static readonly Color32 BoardColor = new Color32(176, 112, 62, 255);
        private static readonly Color32 BoardEdgeColor = new Color32(70, 48, 36, 255);
        private static readonly Color32 FulcrumColor = new Color32(90, 116, 126, 255);

        [MenuItem("CGJ2026/Create Seesaw 7x2 Prefab")]
        public static void CreatePrefab()
        {
            Directory.CreateDirectory(PrefabsRoot);
            Directory.CreateDirectory(ArtRoot);
            Directory.CreateDirectory(PhysicsRoot);

            int worldLayer = EnsureLayer("World");
            Sprite boardSprite = CreateBoardSprite();
            Sprite fulcrumSprite = CreateFulcrumSprite();
            PhysicsMaterial2D material = CreatePhysicsMaterial();

            GameObject root = new GameObject("Seesaw_7x2");
            root.layer = worldLayer;

            GameObject fulcrum = new GameObject("Fulcrum");
            fulcrum.layer = worldLayer;
            fulcrum.transform.SetParent(root.transform, false);
            fulcrum.transform.localPosition = new Vector3(0f, 0.75f, 0f);

            SpriteRenderer fulcrumRenderer = fulcrum.AddComponent<SpriteRenderer>();
            fulcrumRenderer.sprite = fulcrumSprite;
            fulcrumRenderer.drawMode = SpriteDrawMode.Sliced;
            fulcrumRenderer.size = new Vector2(1.2f, 1.5f);
            fulcrumRenderer.sortingOrder = 0;

            PolygonCollider2D fulcrumCollider = fulcrum.AddComponent<PolygonCollider2D>();
            fulcrumCollider.points = new[]
            {
                new Vector2(-0.6f, -0.75f),
                new Vector2(0.6f, -0.75f),
                new Vector2(0f, 0.75f)
            };
            fulcrumCollider.sharedMaterial = material;

            Rigidbody2D fulcrumBody = fulcrum.AddComponent<Rigidbody2D>();
            fulcrumBody.bodyType = RigidbodyType2D.Static;

            GameObject board = new GameObject("Board");
            board.layer = worldLayer;
            board.transform.SetParent(root.transform, false);
            board.transform.localPosition = new Vector3(0f, 1.75f, 0f);

            SpriteRenderer boardRenderer = board.AddComponent<SpriteRenderer>();
            boardRenderer.sprite = boardSprite;
            boardRenderer.drawMode = SpriteDrawMode.Sliced;
            boardRenderer.size = new Vector2(7f, 0.3f);
            boardRenderer.sortingOrder = 1;

            Rigidbody2D boardBody = board.AddComponent<Rigidbody2D>();
            boardBody.bodyType = RigidbodyType2D.Dynamic;
            boardBody.gravityScale = 1f;
            boardBody.mass = 12f;
            boardBody.linearDamping = 0.25f;
            boardBody.angularDamping = 1.75f;
            boardBody.useFullKinematicContacts = false;
            boardBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            boardBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BoxCollider2D boardCollider = board.AddComponent<BoxCollider2D>();
            boardCollider.size = new Vector2(7f, 0.3f);
            boardCollider.sharedMaterial = material;

            HingeJoint2D hinge = board.AddComponent<HingeJoint2D>();
            hinge.connectedBody = fulcrumBody;
            hinge.anchor = Vector2.zero;
            hinge.autoConfigureConnectedAnchor = false;
            hinge.connectedAnchor = fulcrum.transform.InverseTransformPoint(board.transform.position);
            hinge.enableCollision = false;
            hinge.useLimits = true;
            JointAngleLimits2D limits = hinge.limits;
            limits.min = -22f;
            limits.max = 22f;
            hinge.limits = limits;

            SeesawLever2D lever = board.AddComponent<SeesawLever2D>();
            SerializedObject leverObject = new SerializedObject(lever);
            SetObject(leverObject, "body", boardBody);
            SetObject(leverObject, "boardCollider", boardCollider);
            SetObject(leverObject, "hinge", hinge);
            SetFloat(leverObject, "boardLength", 7f);
            SetFloat(leverObject, "boardHeight", 0.3f);
            SetFloat(leverObject, "footprintHeight", 2f);
            SetFloat(leverObject, "maxAngle", 22f);
            SetFloat(leverObject, "boardMass", 12f);
            SetFloat(leverObject, "linearDamping", 0.25f);
            SetFloat(leverObject, "angularDamping", 1.75f);
            SetFloat(leverObject, "minImpactSpeed", 4f);
            SetFloat(leverObject, "minSourceMassRatio", 1.5f);
            SetFloat(leverObject, "minLaunchPower", 18f);
            SetFloat(leverObject, "targetMassResistance", 8f);
            SetFloat(leverObject, "launchImpulsePerPower", 0.55f);
            SetFloat(leverObject, "maxLaunchImpulse", 28f);
            leverObject.ApplyModifiedPropertiesWithoutUndo();

            GameObject pivotMarker = new GameObject("Pivot");
            pivotMarker.layer = worldLayer;
            pivotMarker.transform.SetParent(root.transform, false);
            pivotMarker.transform.localPosition = board.transform.localPosition;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(prefab);
            Debug.Log("Created seesaw prefab at " + PrefabPath + ". Footprint is 7 tiles wide by 2 tiles tall.");
        }

        private static Sprite CreateBoardSprite()
        {
            const int width = 128;
            const int height = 16;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool edge = y < 2 || y >= height - 2 || x < 2 || x >= width - 2;
                    texture.SetPixel(x, y, edge ? BoardEdgeColor : BoardColor);
                }
            }

            return SaveSpriteTexture(texture, BoardSpritePath);
        }

        private static Sprite CreateFulcrumSprite()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                float y01 = y / (float)(size - 1);
                float halfWidth = (1f - y01) * 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float x01 = x / (float)(size - 1);
                    bool inside = Mathf.Abs(x01 - 0.5f) <= halfWidth;
                    texture.SetPixel(x, y, inside ? FulcrumColor : Color.clear);
                }
            }

            return SaveSpriteTexture(texture, FulcrumSpritePath);
        }

        private static Sprite SaveSpriteTexture(Texture2D texture, string path)
        {
            texture.filterMode = FilterMode.Point;
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 1f;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static PhysicsMaterial2D CreatePhysicsMaterial()
        {
            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(PhysicsMaterialPath);
            if (material == null)
            {
                material = new PhysicsMaterial2D("SeesawLowFriction");
                AssetDatabase.CreateAsset(material, PhysicsMaterialPath);
            }

            material.friction = 0.15f;
            material.bounciness = 0f;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetObject(SerializedObject serializedObject, string propertyName, Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetFloat(SerializedObject serializedObject, string propertyName, float value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static int EnsureLayer(string layerName)
        {
            for (int i = 0; i < 32; i++)
            {
                if (LayerMask.LayerToName(i) == layerName)
                {
                    return i;
                }
            }

            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
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

            throw new System.InvalidOperationException("No free Unity layer slot is available for " + layerName + ".");
        }
    }
}
