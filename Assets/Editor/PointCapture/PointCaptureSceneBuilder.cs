using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Votanic.vXR.vGear;

/// <summary>
/// Right-click in the Hierarchy, Project window, or Tools menu to generate the Point Capture
/// placeholder scene: ground, five villages, territory overlay, HUD, and wired troop prefabs.
/// </summary>
public static class PointCaptureSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Point Capture.unity";
    private const string GeneratedRootName = "PointCaptureWorld";
    private const string MaterialsFolder = "Assets/PointCapture/Generated";

    private const string RedInfantryPath = "Assets/Prefabs/foeInfReg.prefab";
    private const string RedArcherPath = "Assets/Prefabs/forArcReg 1_3.prefab";
    private const string YellowInfantryPath = "Assets/Prefabs/ownInfReg.prefab";
    private const string YellowArcherPath = "Assets/Prefabs/ownArcReg.prefab";

    private const float ControlRadius = 12f;
    private const float CorridorWidth = 12f;
    private const float CaptureRadius = ControlRadius;

    [MenuItem("Tools/Point Capture/Bind Wand Commander From vGear")]
    public static void BindWandCommanderFromVgear()
    {
        PointCaptureLocalInput localInput = UnityEngine.Object.FindObjectOfType<PointCaptureLocalInput>();
        if (localInput == null)
        {
            EditorUtility.DisplayDialog(
                "Point Capture",
                "Open the Point Capture scene first (needs PointCaptureLocalInput).",
                "OK");
            return;
        }

        VotanicWandRtsCommander commander = FindExistingVotanicCommander();
        if (commander == null)
        {
            EditorUtility.DisplayDialog(
                "Point Capture",
                "No VotanicWandRtsCommander found on vGear / Controller.\n\n"
                + "Copy the whole vGear root from Hyrule Field into this scene first.",
                "OK");
            return;
        }

        SerializedObject inputSo = new SerializedObject(localInput);
        inputSo.FindProperty("wandCommander").objectReferenceValue = commander;
        inputSo.ApplyModifiedPropertiesWithoutUndo();

        PointCaptureRaiseMenu raiseMenu = UnityEngine.Object.FindObjectOfType<PointCaptureRaiseMenu>();
        if (raiseMenu != null)
        {
            SerializedObject raiseSo = new SerializedObject(raiseMenu);
            raiseSo.FindProperty("wandCommander").objectReferenceValue = commander;
            raiseSo.ApplyModifiedPropertiesWithoutUndo();
        }

        SerializedObject commanderSo = new SerializedObject(commander);
        commanderSo.FindProperty("wandOrigin").objectReferenceValue = null;
        GameObject ground = GameObject.Find("RTS_Ground");
        if (ground != null)
        {
            commanderSo.FindProperty("groundCollider").objectReferenceValue = ground.GetComponent<Collider>();
            int groundLayer = LayerMask.NameToLayer("RTS_Ground");
            if (groundLayer >= 0)
            {
                commanderSo.FindProperty("groundLayers").intValue = 1 << groundLayer;
            }
        }

        commanderSo.ApplyModifiedPropertiesWithoutUndo();

        VotanicWandRtsCommander[] all = UnityEngine.Object.FindObjectsOfType<VotanicWandRtsCommander>(true);
        int disabled = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i] == commander)
            {
                continue;
            }

            if (all[i].GetComponent<PointCaptureLocalInput>() != null
                || all[i].GetComponent<PointCaptureMatch>() != null)
            {
                Undo.RecordObject(all[i], "Disable extra wand commander");
                all[i].enabled = false;
                disabled++;
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog(
            "Point Capture",
            "Bound wand commander: " + GetHierarchyPath(commander.transform)
            + "\nCleared Wand Origin (uses live vGear.controller)."
            + (disabled > 0 ? "\nDisabled " + disabled + " extra commander(s) on Systems." : string.Empty),
            "OK");
    }

    private static VotanicWandRtsCommander FindExistingVotanicCommander()
    {
        VotanicWandRtsCommander[] commanders = UnityEngine.Object.FindObjectsOfType<VotanicWandRtsCommander>(true);
        VotanicWandRtsCommander onController = null;
        VotanicWandRtsCommander onVgear = null;
        VotanicWandRtsCommander any = null;
        for (int i = 0; i < commanders.Length; i++)
        {
            VotanicWandRtsCommander commander = commanders[i];
            if (commander == null)
            {
                continue;
            }

            any = commander;
            if (commander.GetComponent<vGear_Controller>() != null
                || commander.GetComponentInParent<vGear_Controller>() != null)
            {
                onController = commander;
                break;
            }

            Transform current = commander.transform;
            while (current != null)
            {
                if (current.name.IndexOf("vGear", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    onVgear = commander;
                    break;
                }

                current = current.parent;
            }
        }

        if (onController != null)
        {
            return onController;
        }

        return onVgear != null ? onVgear : any;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }
    [MenuItem("Assets/Create/Point Capture/Generate Placeholder Scene Setup", false, 2000)]
    [MenuItem("Tools/Point Capture/Generate Placeholder Scene Setup")]
    public static void GeneratePlaceholderSceneSetup()
    {
        if (!EnsurePointCaptureScene())
        {
            return;
        }

        GenerateIntoOpenScene();
    }

    [MenuItem("GameObject/Point Capture/Generate Placeholders Into Current Scene", false, 11)]
    [MenuItem("Tools/Point Capture/Generate Placeholders Into Current Scene")]
    public static void GenerateIntoCurrentScene()
    {
        Scene active = EditorSceneManager.GetActiveScene();
        if (active.path != ScenePath)
        {
            if (!EditorUtility.DisplayDialog(
                    "Point Capture",
                    "Generate placeholders into the current scene '" + active.name + "'?\n\n"
                    + "This replaces any existing PointCaptureWorld object in this scene.",
                    "Generate Here",
                    "Cancel"))
            {
                return;
            }
        }

        GenerateIntoOpenScene();
    }

    private static bool EnsurePointCaptureScene()
    {
        Scene active = EditorSceneManager.GetActiveScene();
        if (active.path == ScenePath)
        {
            return true;
        }

        bool currentIsSiegeScene = !string.IsNullOrEmpty(active.path)
            && (active.path.IndexOf("Hyrule", System.StringComparison.OrdinalIgnoreCase) >= 0
                || active.path.IndexOf("Battlefield", System.StringComparison.OrdinalIgnoreCase) >= 0
                || active.path.IndexOf("SampleScene", System.StringComparison.OrdinalIgnoreCase) >= 0);

        if (!active.isDirty && (string.IsNullOrEmpty(active.path) || currentIsSiegeScene || active.path != ScenePath))
        {
            if (currentIsSiegeScene || !string.IsNullOrEmpty(active.path))
            {
                if (!EditorUtility.DisplayDialog(
                        "Point Capture",
                        "Create or open '" + ScenePath + "' and generate placeholders there?\n\n"
                        + "The current scene will be left unchanged.",
                        "Create Point Capture Scene",
                        "Cancel"))
                {
                    return false;
                }
            }
        }
        else if (active.isDirty)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Point Capture",
                "The current scene has unsaved changes. Open the Point Capture scene?",
                "Save and Continue",
                "Cancel",
                "Continue Without Saving");
            if (choice == 1)
            {
                return false;
            }

            if (choice == 0 && !EditorSceneManager.SaveOpenScenes())
            {
                return false;
            }
        }

        if (System.IO.File.Exists(ScenePath))
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return true;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            EditorUtility.DisplayDialog("Point Capture", "Could not save " + ScenePath, "OK");
            return false;
        }

        AssetDatabase.Refresh();
        return true;
    }

    private static void GenerateIntoOpenScene()
    {
        EnsureFolders();
        Material groundMaterial = CreateOpaqueMaterial("PointCapture_Ground", new Color(0.36f, 0.48f, 0.32f, 1f));
        Material redMaterial = CreateOpaqueMaterial("PointCapture_Red", CaptureTeams.Red);
        Material yellowMaterial = CreateOpaqueMaterial("PointCapture_Yellow", CaptureTeams.Yellow);
        Material neutralMaterial = CreateOpaqueMaterial("PointCapture_Neutral", CaptureTeams.Neutral);
        Material redFill = CreateTransparentMaterial("PointCapture_TerritoryRed", CaptureTeams.GetColor(CaptureOwner.Red, 0.32f));
        Material yellowFill = CreateTransparentMaterial("PointCapture_TerritoryYellow", CaptureTeams.GetColor(CaptureOwner.Yellow, 0.32f));

        GameObject previous = GameObject.Find(GeneratedRootName);
        if (previous != null)
        {
            Undo.DestroyObjectImmediate(previous);
        }

        GameObject root = new GameObject(GeneratedRootName);
        Undo.RegisterCreatedObjectUndo(root, "Generate Point Capture Placeholders");

        GameObject systems = CreateChild(root.transform, "Systems");
        GameObject villagesRoot = CreateChild(root.transform, "Villages");
        GameObject unitsRoot = CreateChild(root.transform, "Units");

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "RTS_Ground";
        ground.transform.SetParent(root.transform, false);
        ground.transform.localScale = new Vector3(14f, 1f, 14f);
        int groundLayer = LayerMask.NameToLayer("RTS_Ground");
        if (groundLayer >= 0)
        {
            ground.layer = groundLayer;
        }

        MeshRenderer groundRenderer = ground.GetComponent<MeshRenderer>();
        if (groundRenderer != null)
        {
            groundRenderer.sharedMaterial = groundMaterial;
        }

        RtsEnsureGroundColliders groundColliders = ground.AddComponent<RtsEnsureGroundColliders>();
        SerializedObject groundSo = new SerializedObject(groundColliders);
        groundSo.FindProperty("setLayerToRtsGround").boolValue = true;
        groundSo.ApplyModifiedPropertiesWithoutUndo();

        PointCaptureVillage redHome = CreateVillage(
            villagesRoot.transform,
            "Red Home",
            new Vector3(0f, 0f, -34f),
            CaptureOwner.Red,
            redMaterial,
            yellowMaterial,
            neutralMaterial);
        PointCaptureVillage west = CreateVillage(
            villagesRoot.transform,
            "West",
            new Vector3(-26f, 0f, 0f),
            CaptureOwner.Neutral,
            redMaterial,
            yellowMaterial,
            neutralMaterial);
        PointCaptureVillage center = CreateVillage(
            villagesRoot.transform,
            "Center",
            new Vector3(0f, 0f, 0f),
            CaptureOwner.Neutral,
            redMaterial,
            yellowMaterial,
            neutralMaterial);
        PointCaptureVillage east = CreateVillage(
            villagesRoot.transform,
            "East",
            new Vector3(26f, 0f, 0f),
            CaptureOwner.Neutral,
            redMaterial,
            yellowMaterial,
            neutralMaterial);
        PointCaptureVillage yellowHome = CreateVillage(
            villagesRoot.transform,
            "Yellow Home",
            new Vector3(0f, 0f, 34f),
            CaptureOwner.Yellow,
            redMaterial,
            yellowMaterial,
            neutralMaterial);

        PointCaptureVillage[] villages =
        {
            redHome,
            west,
            center,
            east,
            yellowHome
        };

        PointCaptureBoard.Adjacency[] pairs =
        {
            Pair(0, 1),
            Pair(0, 2),
            Pair(0, 3),
            Pair(4, 1),
            Pair(4, 2),
            Pair(4, 3),
            Pair(1, 2),
            Pair(3, 2)
        };

        PointCaptureMatch match = systems.AddComponent<PointCaptureMatch>();
        PointCaptureBoard board = systems.AddComponent<PointCaptureBoard>();
        PointCaptureSpawner spawner = systems.AddComponent<PointCaptureSpawner>();
        systems.AddComponent<PointCaptureNetworkSession>();
        PointCaptureArmyEconomy armyEconomy = systems.AddComponent<PointCaptureArmyEconomy>();
        PointCaptureRaiseMenu raiseMenu = systems.AddComponent<PointCaptureRaiseMenu>();
        PointCaptureLocalInput localInput = systems.AddComponent<PointCaptureLocalInput>();
        PointCaptureHud hud = systems.AddComponent<PointCaptureHud>();
        PointCaptureTerritoryView territory = systems.AddComponent<PointCaptureTerritoryView>();
        RtsCampManager camps = systems.AddComponent<RtsCampManager>();
        SiegePlayEnvironment playEnvironment = systems.AddComponent<SiegePlayEnvironment>();
        VotanicWandRtsCommander commander = FindExistingVotanicCommander();

        SerializedObject boardSo = new SerializedObject(board);
        SerializedProperty villageProp = boardSo.FindProperty("villages");
        villageProp.arraySize = villages.Length;
        for (int i = 0; i < villages.Length; i++)
        {
            villageProp.GetArrayElementAtIndex(i).objectReferenceValue = villages[i];
        }

        SerializedProperty adjProp = boardSo.FindProperty("adjacentPairs");
        adjProp.arraySize = pairs.Length;
        for (int i = 0; i < pairs.Length; i++)
        {
            adjProp.GetArrayElementAtIndex(i).FindPropertyRelative("villageA").intValue = pairs[i].villageA;
            adjProp.GetArrayElementAtIndex(i).FindPropertyRelative("villageB").intValue = pairs[i].villageB;
        }

        boardSo.FindProperty("controlRadius").floatValue = ControlRadius;
        boardSo.FindProperty("corridorWidth").floatValue = CorridorWidth;
        boardSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject campSo = new SerializedObject(camps);
        campSo.FindProperty("friendlyCamp").objectReferenceValue = yellowHome.transform;
        campSo.FindProperty("enemyCamp").objectReferenceValue = redHome.transform;
        campSo.FindProperty("campZoneRadius").floatValue = 8f;
        campSo.FindProperty("campCenterArrivalRadius").floatValue = 1.25f;
        campSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject envSo = new SerializedObject(playEnvironment);
        envSo.FindProperty("playEnvironment").enumValueIndex = (int)SiegePlayEnvironmentMode.Auto;
        envSo.ApplyModifiedPropertiesWithoutUndo();

        GameObject redInfantry = AssetDatabase.LoadAssetAtPath<GameObject>(RedInfantryPath);
        GameObject redArcher = AssetDatabase.LoadAssetAtPath<GameObject>(RedArcherPath);
        GameObject yellowInfantry = AssetDatabase.LoadAssetAtPath<GameObject>(YellowInfantryPath);
        GameObject yellowArcher = AssetDatabase.LoadAssetAtPath<GameObject>(YellowArcherPath);

        SerializedObject spawnerSo = new SerializedObject(spawner);
        spawnerSo.FindProperty("redInfantryPrefab").objectReferenceValue = redInfantry;
        spawnerSo.FindProperty("redArcherPrefab").objectReferenceValue = redArcher;
        spawnerSo.FindProperty("yellowInfantryPrefab").objectReferenceValue = yellowInfantry;
        spawnerSo.FindProperty("yellowArcherPrefab").objectReferenceValue = yellowArcher;
        spawnerSo.FindProperty("unitsRoot").objectReferenceValue = unitsRoot.transform;
        spawnerSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject matchSo = new SerializedObject(match);
        matchSo.FindProperty("board").objectReferenceValue = board;
        matchSo.FindProperty("spawner").objectReferenceValue = spawner;
        matchSo.FindProperty("campManager").objectReferenceValue = camps;
        matchSo.FindProperty("armyEconomy").objectReferenceValue = armyEconomy;
        matchSo.FindProperty("matchDurationSeconds").floatValue = 180f;
        matchSo.FindProperty("baseManpowerPerTenSeconds").floatValue = 5f;
        matchSo.FindProperty("manpowerPerVillagePerTenSeconds").floatValue = 2f;
        matchSo.FindProperty("autoStartOnPlay").boolValue = true;
        matchSo.FindProperty("spawnStartingRegiments").boolValue = true;
        matchSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject economySo = new SerializedObject(armyEconomy);
        economySo.FindProperty("match").objectReferenceValue = match;
        economySo.FindProperty("board").objectReferenceValue = board;
        economySo.FindProperty("idleUpkeepPerTenSeconds").floatValue = 1f;
        economySo.FindProperty("recoveryUpkeepPerSecond").floatValue = 3f;
        economySo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject raiseMenuSo = new SerializedObject(raiseMenu);
        raiseMenuSo.FindProperty("match").objectReferenceValue = match;
        raiseMenuSo.FindProperty("spawner").objectReferenceValue = spawner;
        raiseMenuSo.ApplyModifiedPropertiesWithoutUndo();
        raiseMenu.Configure(match, spawner);

        SerializedObject inputSo = new SerializedObject(localInput);
        inputSo.FindProperty("match").objectReferenceValue = match;
        inputSo.FindProperty("board").objectReferenceValue = board;
        inputSo.FindProperty("spawner").objectReferenceValue = spawner;
        inputSo.FindProperty("raiseMenu").objectReferenceValue = raiseMenu;
        inputSo.FindProperty("wandCommander").objectReferenceValue = commander;
        inputSo.FindProperty("commandFaction").enumValueIndex = (int)CaptureOwner.Yellow;
        inputSo.ApplyModifiedPropertiesWithoutUndo();

        CreateHudCanvas(root.transform, out Text hudText, out Text hudShadow);
        SerializedObject hudSo = new SerializedObject(hud);
        hudSo.FindProperty("match").objectReferenceValue = match;
        hudSo.FindProperty("localInput").objectReferenceValue = localInput;
        hudSo.FindProperty("statusText").objectReferenceValue = hudText;
        hudSo.FindProperty("shadowText").objectReferenceValue = hudShadow;
        hudSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject territorySo = new SerializedObject(territory);
        territorySo.FindProperty("board").objectReferenceValue = board;
        territorySo.ApplyModifiedPropertiesWithoutUndo();
        territory.Configure(board, redFill, yellowFill);

        SetupCameraAndLight();
        if (commander != null)
        {
            SerializedObject commanderSo = new SerializedObject(commander);
            commanderSo.FindProperty("wandOrigin").objectReferenceValue = null;
            commanderSo.FindProperty("enableDesktopFallback").boolValue = true;
            commanderSo.FindProperty("useDesktopMouseRay").boolValue = true;
            Collider groundCollider = ground.GetComponent<Collider>();
            commanderSo.FindProperty("groundCollider").objectReferenceValue = groundCollider;
            if (groundLayer >= 0)
            {
                commanderSo.FindProperty("groundLayers").intValue = 1 << groundLayer;
            }

            commanderSo.ApplyModifiedPropertiesWithoutUndo();
        }

        board.BindVillages();
        for (int i = 0; i < villages.Length; i++)
        {
            villages[i].ResetToStart();
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        string missing = string.Empty;
        if (redInfantry == null)
        {
            missing += "\n- " + RedInfantryPath;
        }

        if (redArcher == null)
        {
            missing += "\n- " + RedArcherPath;
        }

        if (yellowInfantry == null)
        {
            missing += "\n- " + YellowInfantryPath;
        }

        if (yellowArcher == null)
        {
            missing += "\n- " + YellowArcherPath;
        }

        string message = "Point Capture placeholders generated in '"
            + EditorSceneManager.GetActiveScene().path + "'.\n\n"
            + "Press Play, then:\n"
            + "- Left-click drag to command your regiments (Yellow is friendly, Tab switches to Red/foe)\n"
            + "- Right-click a village disc you control to open the raise menu\n"
            + "- MP income: 5 per 10s base + 2 per 10s per village held\n"
            + "- Army upkeep: 1 MP per 10s, or 3 MP/s while recovering in any owned village\n"
            + "- Connecting territory is faded and cannot be used to raise\n"
            + "- 3 minutes, or when one side holds no villages, highest score wins";
        if (!string.IsNullOrEmpty(missing))
        {
            message += "\n\nMissing troop prefabs:" + missing;
        }

        EditorUtility.DisplayDialog("Point Capture", message, "OK");
        Debug.Log("[PointCapture] Generated placeholder scene setup.", root);
    }

    private static PointCaptureBoard.Adjacency Pair(int a, int b)
    {
        return new PointCaptureBoard.Adjacency { villageA = a, villageB = b };
    }

    private static PointCaptureVillage CreateVillage(
        Transform parent,
        string displayName,
        Vector3 position,
        CaptureOwner startingOwner,
        Material redMaterial,
        Material yellowMaterial,
        Material neutralMaterial)
    {
        GameObject villageObject = new GameObject("Village_" + displayName.Replace(" ", string.Empty));
        villageObject.transform.SetParent(parent, false);
        villageObject.transform.position = position;

        GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        building.name = "Building";
        building.transform.SetParent(villageObject.transform, false);
        building.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        building.transform.localScale = new Vector3(4.2f, 1.1f, 4.2f);
        Object.DestroyImmediate(building.GetComponent<Collider>());

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "FlagPole";
        pole.transform.SetParent(villageObject.transform, false);
        pole.transform.localPosition = new Vector3(1.4f, 3.1f, 0f);
        pole.transform.localScale = new Vector3(0.12f, 2.1f, 0.12f);
        Object.DestroyImmediate(pole.GetComponent<Collider>());

        GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
        flag.name = "Flag";
        flag.transform.SetParent(villageObject.transform, false);
        flag.transform.localPosition = new Vector3(2.15f, 4.4f, 0f);
        flag.transform.localScale = new Vector3(1.6f, 0.9f, 0.08f);
        Object.DestroyImmediate(flag.GetComponent<Collider>());

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "CaptureRing";
        ring.transform.SetParent(villageObject.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        ring.transform.localScale = new Vector3(CaptureRadius * 2f, 0.03f, CaptureRadius * 2f);
        Object.DestroyImmediate(ring.GetComponent<Collider>());

        Material startMaterial = startingOwner == CaptureOwner.Red
            ? redMaterial
            : startingOwner == CaptureOwner.Yellow
                ? yellowMaterial
                : neutralMaterial;
        building.GetComponent<MeshRenderer>().sharedMaterial = startMaterial;
        flag.GetComponent<MeshRenderer>().sharedMaterial = startMaterial;
        pole.GetComponent<MeshRenderer>().sharedMaterial = neutralMaterial;
        ring.GetComponent<MeshRenderer>().sharedMaterial = startMaterial;

        TextMesh nameLabel = CreateWorldLabel(villageObject.transform, "NameLabel", new Vector3(0f, 5.4f, 0f), 0.28f);
        TextMesh progressLabel = CreateWorldLabel(villageObject.transform, "ProgressLabel", new Vector3(0f, 4.7f, 0f), 0.22f);

        PointCaptureVillage village = villageObject.AddComponent<PointCaptureVillage>();
        village.Configure(displayName, startingOwner);
        village.AssignVisuals(
            building.GetComponent<MeshRenderer>(),
            flag.GetComponent<MeshRenderer>(),
            ring.GetComponent<MeshRenderer>(),
            nameLabel,
            progressLabel);

        SerializedObject so = new SerializedObject(village);
        so.FindProperty("captureRadius").floatValue = CaptureRadius;
        so.FindProperty("occupyUsingControlDisc").boolValue = true;
        so.FindProperty("captureSeconds").floatValue = 4f;
        so.ApplyModifiedPropertiesWithoutUndo();
        village.ResetToStart();
        return village;
    }

    private static TextMesh CreateWorldLabel(Transform parent, string name, Vector3 localPosition, float characterSize)
    {
        GameObject labelObject = new GameObject(name);
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = localPosition;
        TextMesh textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = 24;
        textMesh.characterSize = characterSize;
        textMesh.color = Color.white;
        textMesh.text = string.Empty;
        return textMesh;
    }

    private static void CreateHudCanvas(Transform parent, out Text text, out Text shadow)
    {
        GameObject canvasObject = new GameObject("PointCaptureHudCanvas");
        canvasObject.transform.SetParent(parent, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject textObject = new GameObject("Status");
        textObject.transform.SetParent(canvasObject.transform, false);
        text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 28;
        text.color = Color.white;
        text.alignment = TextAnchor.UpperLeft;
        text.raycastTarget = false;
        text.text = "Point Capture";
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(28f, -24f);
        rect.sizeDelta = new Vector2(760f, 260f);

        GameObject shadowObject = new GameObject("StatusShadow");
        shadowObject.transform.SetParent(canvasObject.transform, false);
        shadowObject.transform.SetSiblingIndex(0);
        shadow = shadowObject.AddComponent<Text>();
        shadow.font = text.font;
        shadow.fontSize = text.fontSize;
        shadow.color = new Color(0f, 0f, 0f, 0.7f);
        shadow.alignment = text.alignment;
        shadow.raycastTarget = false;
        RectTransform shadowRect = shadow.rectTransform;
        shadowRect.anchorMin = rect.anchorMin;
        shadowRect.anchorMax = rect.anchorMax;
        shadowRect.pivot = rect.pivot;
        shadowRect.anchoredPosition = rect.anchoredPosition + new Vector2(2f, -2f);
        shadowRect.sizeDelta = rect.sizeDelta;
    }

    private static void SetupCameraAndLight()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<AudioListener>();
        }

        camera.transform.position = new Vector3(0f, 48f, -52f);
        camera.transform.rotation = Quaternion.Euler(48f, 0f, 0f);
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 400f;
        camera.fieldOfView = 55f;

        Light light = Object.FindObjectOfType<Light>();
        if (light == null)
        {
            GameObject lightObject = new GameObject("Directional Light");
            light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
        }

        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        light.color = new Color(1f, 0.96f, 0.88f);
        light.intensity = 1.05f;
    }

    private static GameObject CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/PointCapture"))
        {
            AssetDatabase.CreateFolder("Assets", "PointCapture");
        }

        if (!AssetDatabase.IsValidFolder(MaterialsFolder))
        {
            AssetDatabase.CreateFolder("Assets/PointCapture", "Generated");
        }
    }

    private static Material CreateOpaqueMaterial(string assetName, Color color)
    {
        string path = MaterialsFolder + "/" + assetName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Standard");
        if (material == null)
        {
            material = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", 0.15f);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateTransparentMaterial(string assetName, Color color)
    {
        Material material = CreateOpaqueMaterial(assetName, color);
        if (!material.HasProperty("_Mode"))
        {
            return material;
        }

        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }
}
