using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Automation tool for Unity's Occlusion Culling.
/// Automatically identifies static geometry (cave walls, borders, static obstacles),
/// configures Occluder/Occludee static flags, calculates the optimal OcclusionArea bounds,
/// and triggers the Umbra bake.
/// </summary>
public class OcclusionCullingTool : EditorWindow
{
    private float smallestOccluder = 5.0f;
    private float smallestHole = 0.5f;
    private float backfaceThreshold = 100.0f;
    private float areaPadding = 10.0f;

    private Vector2 scrollPos;
    private string lastStatusMessage = "Ready to setup occlusion culling.";
    private MessageType lastStatusType = MessageType.Info;

    [MenuItem("Tools/Space GO/Occlusion Culling Tool", false, 100)]
    public static void ShowWindow()
    {
        OcclusionCullingTool window = GetWindow<OcclusionCullingTool>("Occlusion Culling");
        window.minSize = new Vector2(420, 480);
        window.Show();
    }

    [MenuItem("Tools/Space GO/Setup & Bake Occlusion Culling (One-Click)", false, 101)]
    public static void OneClickSetupAndBake()
    {
        OcclusionCullingTool tool = CreateInstance<OcclusionCullingTool>();
        tool.RunFullPipeline();
        DestroyImmediate(tool);
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Space GO — Occlusion Culling Tool", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Automates marking static occluders (cave walls, borders), creates an optimized " +
            "Occlusion Area, and bakes occlusion culling data without manual hierarchy tagging.",
            MessageType.None);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Bake Parameters", EditorStyles.boldLabel);
        smallestOccluder = EditorGUILayout.Slider(
            new GUIContent("Smallest Occluder (m)", "Smallest object that can hide objects behind it (default: 5m)."),
            smallestOccluder, 1f, 20f);

        smallestHole = EditorGUILayout.Slider(
            new GUIContent("Smallest Hole (m)", "Diameter of smallest opening through which the camera can see (default: 0.5m)."),
            smallestHole, 0.1f, 5f);

        backfaceThreshold = EditorGUILayout.Slider(
            new GUIContent("Backface Threshold (%)", "Percentage of visible backfaces tolerated (100% is standard for opaque caves)."),
            backfaceThreshold, 5f, 100f);

        areaPadding = EditorGUILayout.Slider(
            new GUIContent("Area Padding (m)", "Extra margin around the level bounds for the Occlusion Area volume."),
            areaPadding, 2f, 30f);

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("One-Click Automation", EditorStyles.boldLabel);

        GUI.backgroundColor = new Color(0.3f, 0.85f, 0.4f);
        if (GUILayout.Button(StaticOcclusionCulling.isRunning ? "Baking in progress..." : "1. Setup & Bake All (Recommended)", GUILayout.Height(38)))
        {
            if (!StaticOcclusionCulling.isRunning)
            {
                RunFullPipeline();
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("Individual Operations", EditorStyles.boldLabel);

        if (GUILayout.Button("Step 1: Flag Static Geometry Only"))
        {
            int count = FlagSceneGeometry();
            SetStatus($"Successfully applied Occluder/Occludee flags to {count} GameObjects.", MessageType.Info);
        }

        if (GUILayout.Button("Step 2: Setup / Resize Occlusion Area"))
        {
            OcclusionArea area = SetupOcclusionArea(areaPadding);
            if (area != null)
            {
                SetStatus($"Occlusion Area positioned at {area.transform.position + area.center} with size {area.size}.", MessageType.Info);
            }
        }

        if (GUILayout.Button("Step 3: Trigger Bake Only"))
        {
            ApplyBakeSettings();
            if (StaticOcclusionCulling.Compute())
            {
                SetStatus("Occlusion bake started in background.", MessageType.Info);
            }
            else
            {
                SetStatus("Failed to start occlusion bake. Verify static occluders exist.", MessageType.Warning);
            }
        }

        EditorGUILayout.Space(10);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("Clear Baked Occlusion Data"))
        {
            if (EditorUtility.DisplayDialog("Clear Occlusion Data?", "Are you sure you want to clear the baked occlusion data?", "Yes, Clear", "Cancel"))
            {
                StaticOcclusionCulling.Clear();
                SetStatus("Baked occlusion culling data cleared.", MessageType.Warning);
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(15);
        EditorGUILayout.HelpBox(lastStatusMessage, lastStatusType);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Verification Guide:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("1. Open Window > Rendering > Occlusion Culling.");
        EditorGUILayout.LabelField("2. Switch to the 'Visualization' tab.");
        EditorGUILayout.LabelField("3. In Scene View, move the camera along the cave to see occluded chunks vanish.");

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// Executes the full pipeline: marks static flags, builds OcclusionArea, and computes the bake.
    /// </summary>
    public void RunFullPipeline()
    {
        int flaggedCount = FlagSceneGeometry();
        OcclusionArea area = SetupOcclusionArea(areaPadding);
        ApplyBakeSettings();

        bool started = StaticOcclusionCulling.Compute();

        if (started)
        {
            SetStatus(
                $"Pipeline complete! Flagged {flaggedCount} objects, fitted OcclusionArea, and started bake.",
                MessageType.Info);
        }
        else
        {
            SetStatus(
                $"Flagged {flaggedCount} objects, but bake could not be started. Check Unity Console for details.",
                MessageType.Warning);
        }
    }

    /// <summary>
    /// Identifies all static level geometry and sets OccluderStatic and OccludeeStatic flags.
    /// </summary>
    public static int FlagSceneGeometry()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] rootObjects = activeScene.GetRootGameObjects();

        int flaggedCount = 0;
        HashSet<GameObject> objectsToFlag = new HashSet<GameObject>();

        foreach (GameObject root in rootObjects)
        {
            Transform[] allChildren = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allChildren)
            {
                GameObject go = t.gameObject;

                // Never flag dynamic objects (player, missile, pickups, moving hazards)
                if (IsDynamicObject(go))
                {
                    continue;
                }

                // Identify static environment pieces
                if (IsStaticLevelGeometry(go))
                {
                    objectsToFlag.Add(go);
                }
            }
        }

        // Apply StaticEditorFlags
        StaticEditorFlags targetFlags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Set Occlusion Static Flags");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (GameObject go in objectsToFlag)
        {
            StaticEditorFlags current = GameObjectUtility.GetStaticEditorFlags(go);
            if ((current & targetFlags) != targetFlags)
            {
                Undo.RecordObject(go, "Set Occlusion Flags");
                GameObjectUtility.SetStaticEditorFlags(go, current | targetFlags);
                flaggedCount++;
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (flaggedCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            Debug.Log($"[OcclusionCullingTool] Marked {flaggedCount} static GameObjects as Occluder & Occludee Static.");
        }
        else
        {
            Debug.Log("[OcclusionCullingTool] All static geometry already had Occluder & Occludee flags set.");
        }

        return flaggedCount;
    }

    /// <summary>
    /// Creates or updates an OcclusionArea covering the entire level geometry bounds.
    /// </summary>
    public static OcclusionArea SetupOcclusionArea(float padding = 10f)
    {
        Scene activeScene = SceneManager.GetActiveScene();

        // 1. Calculate global bounds across all non-UI 3D mesh renderers
        Renderer[] allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        bool hasBounds = false;
        Bounds levelBounds = new Bounds(Vector3.zero, Vector3.zero);

        int uiLayer = LayerMask.NameToLayer("UI");

        foreach (Renderer r in allRenderers)
        {
            if (r == null || !r.enabled || r.gameObject.layer == uiLayer) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

            if (!hasBounds)
            {
                levelBounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                levelBounds.Encapsulate(r.bounds);
            }
        }

        if (!hasBounds)
        {
            Debug.LogWarning("[OcclusionCullingTool] No 3D renderers found to calculate OcclusionArea bounds.");
            return null;
        }

        // Expand bounds slightly so the camera is fully covered inside the volume
        levelBounds.Expand(padding * 2f);

        // 2. Find or create the OcclusionArea GameObject
        OcclusionArea area = Object.FindAnyObjectByType<OcclusionArea>();
        GameObject areaGO;

        if (area == null)
        {
            areaGO = new GameObject("Level_OcclusionArea");
            Undo.RegisterCreatedObjectUndo(areaGO, "Create Occlusion Area");
            area = areaGO.AddComponent<OcclusionArea>();
        }
        else
        {
            areaGO = area.gameObject;
            Undo.RecordObject(area, "Update Occlusion Area");
            Undo.RecordObject(areaGO.transform, "Update Occlusion Area Transform");
        }

        // Position the GameObject at bounds center and set local center to zero
        areaGO.transform.position = levelBounds.center;
        areaGO.transform.rotation = Quaternion.identity;
        areaGO.transform.localScale = Vector3.one;

        area.center = Vector3.zero;
        area.size = levelBounds.size;

        EditorSceneManager.MarkSceneDirty(activeScene);
        Debug.Log($"[OcclusionCullingTool] Fitted OcclusionArea at {levelBounds.center} with size {levelBounds.size}.");

        return area;
    }

    private void ApplyBakeSettings()
    {
        StaticOcclusionCulling.smallestOccluder   = smallestOccluder;
        StaticOcclusionCulling.smallestHole       = smallestHole;
        StaticOcclusionCulling.backfaceThreshold = backfaceThreshold;
        Debug.Log($"[OcclusionCullingTool] Configured bake settings: Smallest Occluder={smallestOccluder}m, Smallest Hole={smallestHole}m, Backface Threshold={backfaceThreshold}%.");
    }

    private static bool IsStaticLevelGeometry(GameObject go)
    {
        string name = go.name.ToLower();

        // Cave walls and tunnels
        if (name.Contains("cavewall") || name.Contains("cave_wall") || name.Contains("cavewalls1"))
            return true;

        // Borders and boundaries
        if (name.Contains("border") || name.Contains("boundary") || name.Contains("perimeter"))
            return true;

        // Static structures and landing pads
        if (name.Contains("landingpad") || name.Contains("finishpad") || name.Contains("platform"))
            return true;

        // Static obstacle cylinders, columns, blocks
        if (name.Contains("cylinder") || name.Contains("column") || name.Contains("pillar") || name.Contains("rock"))
            return true;

        return false;
    }

    private static bool IsDynamicObject(GameObject go)
    {
        // Tag-based dynamic entities
        if (go.CompareTag("Player") || go.CompareTag("Respawn"))
            return true;

        string name = go.name.ToLower();

        // Missile, player, pickups
        if (name.Contains("missile") || name.Contains("player") || name.Contains("pickup") || name.Contains("rocket"))
            return true;

        // Dynamic moving hazards and triggers
        if (go.GetComponent<Rigidbody>() != null)
            return true;
        if (go.GetComponent<Oscillator>() != null || go.GetComponent<RotatingHazard>() != null)
            return true;
        if (go.GetComponent<CrushingHazard>() != null || go.GetComponent<LaserGate>() != null)
            return true;
        if (go.GetComponent<FuelPickup>() != null || go.GetComponent<RepairPickup>() != null)
            return true;
        if (go.GetComponent<CheckpointZone>() != null)
            return true;

        return false;
    }

    private void SetStatus(string message, MessageType type)
    {
        lastStatusMessage = message;
        lastStatusType = type;
        Repaint();
    }
}
