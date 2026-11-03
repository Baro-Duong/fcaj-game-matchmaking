using System.Collections.Generic;
using System.IO;
using MM.Gameplay;
using MM.Net;
using MM.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Editor.Configuration;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MM.EditorTools
{
    // Editor only (menu MM/Setup/Build Main Scene). Creates Main.unity, the Player prefab and every
    // reference between them in one click: reproducible, reviewable in code, and no 40-step manual checklist.
    public static class MainSceneBuilder
    {
        const string k_Root = "Assets/_Project";
        const string k_ScenePath = k_Root + "/Scenes/Main.unity";
        const string k_PlayerPrefabPath = k_Root + "/Prefabs/Player.prefab";
        const string k_OwnPrefabListPath = k_Root + "/Prefabs/NetworkPrefabs.asset";
        const string k_SquarePath = k_Root + "/Art/Square.png";
        const string k_TemplateScenePath = "Assets/Scenes/SampleScene.unity";
        // Unlit so the squares do not depend on 2D lights.
        const string k_UnlitSpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        const ushort k_Port = 7777;
        const uint k_TickRate = 30;

        static TMP_DefaultControls.Resources s_UiResources;

        [MenuItem("MM/Setup/Build Main Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Build Main Scene", "Exit Play Mode first.", "OK");
                return;
            }

            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
            {
                EditorUtility.DisplayDialog("Build Main Scene",
                    "TextMesh Pro resources are missing.\n\nWindow > TextMeshPro > Import TMP Essential Resources, then run this again.", "OK");
                return;
            }

            if (File.Exists(k_ScenePath) &&
                !EditorUtility.DisplayDialog("Build Main Scene", "Main.unity already exists. Rebuild it? Manual edits in that scene will be lost.", "Rebuild", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(k_Root, "Scenes");
            EnsureFolder(k_Root, "Prefabs");
            EnsureFolder(k_Root, "Art");
            s_UiResources = LoadUiResources();

            Sprite square = EnsureSquareSprite();
            GameObject playerPrefab = BuildPlayerPrefab(square);
            NetworkPrefabsList prefabList = EnsurePrefabList(playerPrefab);

            Scene scene = CreateSceneFromTemplate();
            NetworkBootstrap bootstrap = CreateNetworkManager(playerPrefab, prefabList);
            CreateMatchManager();
            CreateEventSystem();
            CreateUi(bootstrap);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Reopen so in-scene NetworkObject ids are computed against Main.unity's own GUID, then save that.
            scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(k_ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[MM] Built {k_ScenePath} + {k_PlayerPrefabPath}; scene list = Main only");
        }

        // ---------- assets ----------

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }

        static Sprite EnsureSquareSprite()
        {
            if (!File.Exists(k_SquarePath))
            {
                const int size = 16;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(k_SquarePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(k_SquarePath);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(k_SquarePath);
            if (importer.textureType != TextureImporterType.Sprite || !Mathf.Approximately(importer.spritePixelsPerUnit, 16f))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 16f;   // 16 px texture = 1 world unit
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(k_SquarePath);
        }

        static GameObject BuildPlayerPrefab(Sprite square)
        {
            var go = new GameObject("Player");
            go.transform.localScale = new Vector3(2f, 2f, 1f);

            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = square;
            var unlit = AssetDatabase.LoadAssetAtPath<Material>(k_UnlitSpriteMaterialPath);
            if (unlit != null) spriteRenderer.sharedMaterial = unlit;

            go.AddComponent<NetworkObject>();
            var presser = go.AddComponent<PlayerPresser>();
            SetRef(presser, "m_Renderer", spriteRenderer);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, k_PlayerPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static NetworkPrefabsList EnsurePrefabList(GameObject playerPrefab)
        {
            // NGO auto-maintains a default list (Assets/DefaultNetworkPrefabs.asset); reuse it so the
            // prefab is never registered twice. Fall back to our own list if that feature is turned off.
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabProcessor.DefaultNetworkPrefabsPath);
            if (list == null) list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(k_OwnPrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, k_OwnPrefabListPath);
            }

            if (!list.Contains(playerPrefab))
            {
                list.Add(new NetworkPrefab { Prefab = playerPrefab });
                EditorUtility.SetDirty(list);
            }

            return list;
        }

        // ---------- scene objects ----------

        static Scene CreateSceneFromTemplate()
        {
            Scene scene;
            if (File.Exists(k_TemplateScenePath))
            {
                // The URP 2D template scene already has a configured 2D camera + Global Light 2D.
                scene = EditorSceneManager.OpenScene(k_TemplateScenePath, OpenSceneMode.Single);
                EditorSceneManager.SaveScene(scene, k_ScenePath);   // "save as": the template stays untouched
                scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, k_ScenePath);
            }

            var camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            }

            return scene;
        }

        static NetworkBootstrap CreateNetworkManager(GameObject playerPrefab, NetworkPrefabsList prefabList)
        {
            var go = new GameObject("NetworkManager");
            var networkManager = go.AddComponent<NetworkManager>();
            // NetworkManager may add a transport by itself in the editor; never end up with two.
            var transport = go.GetComponent<UnityTransport>();
            if (transport == null) transport = go.AddComponent<UnityTransport>();
            var bootstrap = go.AddComponent<NetworkBootstrap>();

            if (networkManager.NetworkConfig == null) networkManager.NetworkConfig = new NetworkConfig();
            NetworkConfig config = networkManager.NetworkConfig;
            config.NetworkTransport = transport;
            config.NetworkTopology = NetworkTopologyTypes.ClientServer;
            config.PlayerPrefab = playerPrefab;
            config.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { prefabList };
            config.TickRate = k_TickRate;
            config.ConnectionApproval = true;

            transport.ConnectionData.Address = "127.0.0.1";
            transport.ConnectionData.Port = k_Port;
            transport.ConnectionData.ServerListenAddress = "0.0.0.0";
            // Default is 60 attempts x 1 s; fail after ~10 s so a wrong IP gives quick feedback.
            transport.MaxConnectAttempts = 10;

            SetRef(bootstrap, "m_NetworkManager", networkManager);
            SetRef(bootstrap, "m_Transport", transport);
            EditorUtility.SetDirty(networkManager);
            EditorUtility.SetDirty(transport);
            return bootstrap;
        }

        static void CreateMatchManager()
        {
            var go = new GameObject("MatchManager");
            go.AddComponent<NetworkObject>();
            go.AddComponent<MatchManager>();
        }

        static void CreateEventSystem()
        {
            // The project uses the new Input System only, so the old StandaloneInputModule would throw.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // ---------- UI ----------

        static void CreateUi(NetworkBootstrap bootstrap)
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.layer = LayerMask.NameToLayer("UI");
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960f, 540f);
            scaler.matchWidthOrHeight = 0.5f;

            // HUD first so the connection menu draws on top of it.
            CreateHud(canvasGo.transform);
            CreateConnectionPanel(canvasGo.transform, bootstrap);
        }

        static void CreateHud(Transform canvas)
        {
            var hudGo = CreateRect("Hud", canvas);
            Stretch(hudGo);
            var hud = hudGo.AddComponent<MatchHud>();

            var root = CreateRect("HudRoot", hudGo.transform);
            Stretch(root);

            var left = CreateText("LeftScore", root.transform, "", 40f);
            Place(left.rectTransform, new Vector2(0.25f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(300f, 110f));
            var right = CreateText("RightScore", root.transform, "", 40f);
            Place(right.rectTransform, new Vector2(0.75f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(300f, 110f));
            var timer = CreateText("Timer", root.transform, "", 56f);
            Place(timer.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(200f, 80f));
            var message = CreateText("Message", root.transform, "", 48f);
            Place(message.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(800f, 130f));

            SetRef(hud, "m_Root", root);
            SetRef(hud, "m_LeftScoreText", left);
            SetRef(hud, "m_RightScoreText", right);
            SetRef(hud, "m_TimerText", timer);
            SetRef(hud, "m_MessageText", message);
        }

        static void CreateConnectionPanel(Transform canvas, NetworkBootstrap bootstrap)
        {
            var panelGo = CreateRect("ConnectionPanel", canvas);
            Stretch(panelGo);
            var panel = panelGo.AddComponent<ConnectionPanel>();

            // Centered menu box laid out top to bottom.
            var menu = CreateRect("Menu", panelGo.transform);
            Place((RectTransform)menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 300f));
            var menuImage = menu.AddComponent<Image>();
            menuImage.sprite = s_UiResources.background;
            menuImage.type = Image.Type.Sliced;
            menuImage.color = new Color(0.16f, 0.18f, 0.24f, 0.95f);
            var column = menu.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(20, 20, 16, 16);
            column.spacing = 10f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            var title = CreateText("Title", menu.transform, "SPACE DUEL", 36f);
            SetHeight(title.gameObject, 50f);
            var ipInput = CreateInput("IpInput", menu.transform, "Server IP", TMP_InputField.ContentType.Standard);
            var portInput = CreateInput("PortInput", menu.transform, "Port", TMP_InputField.ContentType.IntegerNumber);

            var row = CreateRect("Buttons", menu.transform);
            SetHeight(row, 44f);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            var hostButton = CreateButton("HostButton", row.transform, "Host");
            var clientButton = CreateButton("ClientButton", row.transform, "Client");
            var serverButton = CreateButton("ServerButton", row.transform, "Server");

            var hint = CreateText("Hint", menu.transform, "Host = play + serve. Press SPACE as fast as you can.", 16f);
            hint.color = new Color(1f, 1f, 1f, 0.7f);
            SetHeight(hint.gameObject, 40f);

            // Outside the menu so messages stay visible after the menu hides (e.g. "Disconnected: full").
            var status = CreateText("Status", panelGo.transform, "", 20f);
            Place(status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(900f, 40f));

            var leaveButton = CreateButton("LeaveButton", panelGo.transform, "Leave");
            Place((RectTransform)leaveButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 16f), new Vector2(120f, 40f));

            SetRef(panel, "m_Bootstrap", bootstrap);
            SetRef(panel, "m_MenuRoot", menu);
            SetRef(panel, "m_IpInput", ipInput);
            SetRef(panel, "m_PortInput", portInput);
            SetRef(panel, "m_HostButton", hostButton);
            SetRef(panel, "m_ClientButton", clientButton);
            SetRef(panel, "m_ServerButton", serverButton);
            SetRef(panel, "m_LeaveButton", leaveButton);
            SetRef(panel, "m_StatusText", status);
        }

        // ---------- UI helpers ----------

        static TMP_DefaultControls.Resources LoadUiResources()
        {
            return new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };
        }

        static GameObject CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return go;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize)
        {
            var go = TMP_DefaultControls.CreateText(s_UiResources);
            go.name = name;
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        static TMP_InputField CreateInput(string name, Transform parent, string placeholder, TMP_InputField.ContentType contentType)
        {
            var go = TMP_DefaultControls.CreateInputField(s_UiResources);
            go.name = name;
            go.transform.SetParent(parent, false);
            SetHeight(go, 40f);
            var input = go.GetComponent<TMP_InputField>();
            input.contentType = contentType;
            input.pointSize = 20f;
            if (input.placeholder is TMP_Text placeholderText) placeholderText.text = placeholder;
            return input;
        }

        static Button CreateButton(string name, Transform parent, string label)
        {
            var go = TMP_DefaultControls.CreateButton(s_UiResources);
            go.name = name;
            go.transform.SetParent(parent, false);
            var text = go.GetComponentInChildren<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 20f;
            return go.GetComponent<Button>();
        }

        static void SetHeight(GameObject go, float height)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
        }

        static void Stretch(GameObject go)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // Private [SerializeField] fields can only be assigned from editor code through SerializedObject.
        static void SetRef(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[MM] {target.GetType().Name} has no serialized field '{fieldName}'");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
