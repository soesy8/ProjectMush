using System;
using System.Collections.Generic;
using System.Linq;
using Mush.EditorTools.PrefabScatter;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mush.EditorTools.PrefabScatter.Editor
{
    internal enum ScatterPlacementMode
    {
        Single,
        Brush
    }

    [FilePath("Library/PrefabScatterToolSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class PrefabScatterSettings : ScriptableSingleton<PrefabScatterSettings>
    {
        public List<GameObject> prefabs = new List<GameObject>();
        public float radius = 3f;
        public int density = 5;
        public float spacing = 1f;
        public Vector2 rotationX = Vector2.zero;
        public Vector2 rotationY = new Vector2(0f, 360f);
        public Vector2 rotationZ = Vector2.zero;
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
        public bool alignToNormal = true;
        public LayerMask surfaceMask = ~0;
        public Vector2 slopeRange = new Vector2(0f, 50f);
        public Vector2 heightRange = new Vector2(-10000f, 10000f);
        public ScatterPlacementMode placementMode = ScatterPlacementMode.Brush;
        public bool eraseMode;
        public Transform parent;

        public void SaveSettings()
        {
            Save(true);
        }
    }

    public sealed class PrefabScatterSettingsWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Tools/Prefab Scatter/Settings")]
        public static void ShowWindow()
        {
            GetWindow<PrefabScatterSettingsWindow>("Prefab Scatter");
        }

        private void OnGUI()
        {
            PrefabScatterSettings settings = PrefabScatterSettings.instance;
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Prefab Scatter Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Left Click: place  |  Left Drag: paint  |  Shift + Left Click: erase  |  Ctrl/Cmd + Wheel: resize brush",
                MessageType.Info);

            if (GUILayout.Button("Activate Scene Tool", GUILayout.Height(28f)))
                ToolManager.SetActiveTool<PrefabScatterTool>();

            EditorGUILayout.Space(8f);
            EditorGUI.BeginChangeCheck();

            DrawPrefabList(settings);

            EditorGUILayout.Space(6f);
            settings.placementMode = (ScatterPlacementMode)EditorGUILayout.EnumPopup("Placement", settings.placementMode);
            settings.eraseMode = EditorGUILayout.Toggle("Erase Mode", settings.eraseMode);
            settings.radius = Mathf.Max(0.05f, EditorGUILayout.FloatField("Radius", settings.radius));
            settings.density = Mathf.Clamp(EditorGUILayout.IntField("Density / Stamp", settings.density), 1, 200);
            settings.spacing = Mathf.Max(0f, EditorGUILayout.FloatField("Minimum Spacing", settings.spacing));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Random Transform", EditorStyles.boldLabel);
            settings.rotationX = DrawOrderedRange("Rotation X", settings.rotationX, -360f, 360f);
            settings.rotationY = DrawOrderedRange("Rotation Y", settings.rotationY, -360f, 360f);
            settings.rotationZ = DrawOrderedRange("Rotation Z", settings.rotationZ, -360f, 360f);
            settings.scaleRange = DrawOrderedRange("Uniform Scale", settings.scaleRange, 0.01f, 100f);
            settings.alignToNormal = EditorGUILayout.Toggle("Align To Normal", settings.alignToNormal);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Surface Filters", EditorStyles.boldLabel);
            settings.surfaceMask = DrawLayerMask("Surface Layers", settings.surfaceMask);
            settings.slopeRange = DrawOrderedRange("Slope (Degrees)", settings.slopeRange, 0f, 180f);
            settings.heightRange = DrawOrderedRange("World Height", settings.heightRange, -100000f, 100000f);
            settings.parent = (Transform)EditorGUILayout.ObjectField("Parent", settings.parent, typeof(Transform), true);

            if (EditorGUI.EndChangeCheck())
            {
                settings.SaveSettings();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(8f);
            DrawValidation(settings);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawPrefabList(PrefabScatterSettings settings)
        {
            EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel);

            for (int i = 0; i < settings.prefabs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                settings.prefabs[i] = (GameObject)EditorGUILayout.ObjectField(
                    $"Prefab {i + 1}", settings.prefabs[i], typeof(GameObject), false);

                if (GUILayout.Button("-", GUILayout.Width(24f)))
                {
                    settings.prefabs.RemoveAt(i);
                    GUI.changed = true;
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Prefab"))
            {
                settings.prefabs.Add(null);
                GUI.changed = true;
            }
        }

        private static Vector2 DrawOrderedRange(string label, Vector2 value, float minimum, float maximum)
        {
            value = EditorGUILayout.Vector2Field(label, value);
            value.x = Mathf.Clamp(value.x, minimum, maximum);
            value.y = Mathf.Clamp(value.y, minimum, maximum);
            if (value.x > value.y)
                (value.x, value.y) = (value.y, value.x);
            return value;
        }

        private static LayerMask DrawLayerMask(string label, LayerMask value)
        {
            string[] layerNames = new string[32];
            for (int i = 0; i < layerNames.Length; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                layerNames[i] = string.IsNullOrEmpty(layerName) ? $"{i}: (Unnamed)" : $"{i}: {layerName}";
            }

            return EditorGUILayout.MaskField(label, value.value, layerNames);
        }

        private static void DrawValidation(PrefabScatterSettings settings)
        {
            int validPrefabCount = settings.prefabs.Count(prefab =>
                prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab));

            if (validPrefabCount == 0)
                EditorGUILayout.HelpBox("Add at least one prefab asset before painting.", MessageType.Warning);

            if (settings.surfaceMask.value == 0)
                EditorGUILayout.HelpBox("Surface Layers is empty, so the brush cannot hit a surface.", MessageType.Warning);
        }
    }

    [EditorTool("Prefab Scatter")]
    public sealed class PrefabScatterTool : EditorTool
    {
        private const float MinRadius = 0.05f;
        private const float MaxRadius = 10000f;
        private static readonly Color PlaceColor = new Color(0.25f, 0.9f, 0.35f, 0.95f);
        private static readonly Color EraseColor = new Color(1f, 0.25f, 0.2f, 0.95f);

        private bool isPainting;
        private bool hasLastStampPosition;
        private Vector3 lastStampPosition;

        public override GUIContent toolbarIcon
        {
            get
            {
                GUIContent icon = EditorGUIUtility.IconContent("TerrainInspector.TerrainToolTrees");
                return new GUIContent(icon.image, "Prefab Scatter");
            }
        }

        public override void OnActivated()
        {
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            isPainting = false;
            hasLastStampPosition = false;
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView))
                return;

            Event current = Event.current;
            PrefabScatterSettings settings = PrefabScatterSettings.instance;

            if (current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if ((current.control || current.command) && current.type == EventType.ScrollWheel)
            {
                float multiplier = current.delta.y > 0f ? 0.9f : 1.1f;
                settings.radius = Mathf.Clamp(settings.radius * multiplier, MinRadius, MaxRadius);
                settings.SaveSettings();
                current.Use();
                SceneView.RepaintAll();
                return;
            }

            bool hasSurface = TryGetSurfaceUnderMouse(current.mousePosition, settings.surfaceMask, out RaycastHit surfaceHit);
            bool erase = settings.eraseMode || current.shift;

            if (hasSurface)
            {
                DrawBrush(surfaceHit, settings.radius, erase);
                DrawSceneHud(settings, erase);
            }

            if (current.alt || current.button != 0)
                return;

            if (current.type == EventType.MouseDown)
            {
                isPainting = true;
                hasLastStampPosition = false;

                if (hasSurface)
                {
                    ApplyStamp(surfaceHit, settings, erase);
                    lastStampPosition = surfaceHit.point;
                    hasLastStampPosition = true;
                }

                current.Use();
            }
            else if (current.type == EventType.MouseDrag && isPainting)
            {
                if (hasSurface && settings.placementMode == ScatterPlacementMode.Brush)
                {
                    float stampDistance = Mathf.Max(settings.radius * 0.2f, settings.spacing * 0.5f, 0.05f);
                    if (!hasLastStampPosition || Vector3.Distance(lastStampPosition, surfaceHit.point) >= stampDistance)
                    {
                        ApplyStamp(surfaceHit, settings, erase);
                        lastStampPosition = surfaceHit.point;
                        hasLastStampPosition = true;
                    }
                }

                current.Use();
            }
            else if (current.type == EventType.MouseUp && isPainting)
            {
                isPainting = false;
                hasLastStampPosition = false;
                current.Use();
            }
        }

        private static void DrawBrush(RaycastHit hit, float radius, bool erase)
        {
            Color oldColor = Handles.color;
            Handles.color = erase ? EraseColor : PlaceColor;
            Handles.DrawWireDisc(hit.point, hit.normal, radius, 2f);
            Handles.DrawLine(hit.point, hit.point + hit.normal * Mathf.Min(radius * 0.35f, 1f), 2f);
            Handles.color = oldColor;
        }

        private static void DrawSceneHud(PrefabScatterSettings settings, bool erase)
        {
            Handles.BeginGUI();
            Rect rect = new Rect(12f, 12f, 245f, 58f);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(22f, 18f, 220f, 20f), erase ? "Prefab Scatter — ERASE" : "Prefab Scatter — PLACE", EditorStyles.boldLabel);
            GUI.Label(new Rect(22f, 40f, 220f, 20f), $"Radius {settings.radius:0.##}  |  Density {settings.density}  |  Spacing {settings.spacing:0.##}");
            Handles.EndGUI();
        }

        private static void ApplyStamp(RaycastHit centerHit, PrefabScatterSettings settings, bool erase)
        {
            if (erase)
            {
                EraseAt(centerHit.point, settings.radius, centerHit.collider.gameObject.scene);
                return;
            }

            List<GameObject> validPrefabs = settings.prefabs
                .Where(prefab => prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab))
                .ToList();

            if (validPrefabs.Count == 0)
                return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint Prefab Scatter");

            if (settings.placementMode == ScatterPlacementMode.Single)
            {
                TryPlace(centerHit, settings, validPrefabs);
            }
            else
            {
                int placed = 0;
                int attempts = 0;
                int maxAttempts = Mathf.Max(settings.density * 15, 15);

                while (placed < settings.density && attempts++ < maxAttempts)
                {
                    Vector2 point = UnityEngine.Random.insideUnitCircle * settings.radius;
                    Vector3 tangent = Vector3.Cross(centerHit.normal, Vector3.up);
                    if (tangent.sqrMagnitude < 0.0001f)
                        tangent = Vector3.Cross(centerHit.normal, Vector3.right);
                    tangent.Normalize();
                    Vector3 bitangent = Vector3.Cross(centerHit.normal, tangent).normalized;
                    Vector3 candidate = centerHit.point + tangent * point.x + bitangent * point.y;

                    float castOffset = Mathf.Max(settings.radius, 1f) + 1f;
                    Ray ray = new Ray(candidate + centerHit.normal * castOffset, -centerHit.normal);
                    if (!TryRaycastPaintSurface(ray, castOffset * 2f, settings.surfaceMask, out RaycastHit hit))
                        continue;

                    if (Vector3.Distance(hit.point, centerHit.point) > settings.radius * 1.25f)
                        continue;

                    if (TryPlace(hit, settings, validPrefabs))
                        placed++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
        }

        private static bool TryPlace(RaycastHit hit, PrefabScatterSettings settings, IReadOnlyList<GameObject> prefabs)
        {
            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (slope < settings.slopeRange.x || slope > settings.slopeRange.y)
                return false;

            if (hit.point.y < settings.heightRange.x || hit.point.y > settings.heightRange.y)
                return false;

            Scene targetScene = hit.collider.gameObject.scene;
            if (settings.spacing > 0f && IsTooCloseToExisting(hit.point, settings.spacing, targetScene))
                return false;

            GameObject prefab = prefabs[UnityEngine.Random.Range(0, prefabs.Count)];
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, targetScene) as GameObject;
            if (instance == null)
                return false;

            Transform instanceTransform = instance.transform;
            if (settings.parent != null && settings.parent.gameObject.scene == targetScene)
                instanceTransform.SetParent(settings.parent, true);

            instanceTransform.position = hit.point;

            Quaternion randomRotation = Quaternion.Euler(
                UnityEngine.Random.Range(settings.rotationX.x, settings.rotationX.y),
                UnityEngine.Random.Range(settings.rotationY.x, settings.rotationY.y),
                UnityEngine.Random.Range(settings.rotationZ.x, settings.rotationZ.y));
            instanceTransform.rotation = settings.alignToNormal
                ? Quaternion.FromToRotation(Vector3.up, hit.normal) * randomRotation
                : randomRotation;

            float uniformScale = UnityEngine.Random.Range(settings.scaleRange.x, settings.scaleRange.y);
            instanceTransform.localScale = Vector3.Scale(instanceTransform.localScale, Vector3.one * uniformScale);

            PrefabScatterMarker marker = instance.GetComponent<PrefabScatterMarker>();
            if (marker == null)
                marker = instance.AddComponent<PrefabScatterMarker>();
            marker.sourcePrefab = prefab;
            marker.hideFlags = HideFlags.HideInInspector;

            Undo.RegisterCreatedObjectUndo(instance, "Place Scattered Prefab");
            EditorUtility.SetDirty(instance);
            return true;
        }

        private static bool IsTooCloseToExisting(Vector3 point, float spacing, Scene targetScene)
        {
            float squaredSpacing = spacing * spacing;
            PrefabScatterMarker[] markers = UnityEngine.Object.FindObjectsByType<PrefabScatterMarker>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (PrefabScatterMarker marker in markers)
            {
                if (marker != null &&
                    marker.gameObject.scene == targetScene &&
                    (marker.transform.position - point).sqrMagnitude < squaredSpacing)
                    return true;
            }

            return false;
        }

        private static void EraseAt(Vector3 center, float radius, Scene targetScene)
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Erase Prefab Scatter");
            float squaredRadius = radius * radius;

            PrefabScatterMarker[] markers = UnityEngine.Object.FindObjectsByType<PrefabScatterMarker>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (PrefabScatterMarker marker in markers)
            {
                if (marker != null &&
                    marker.gameObject.scene == targetScene &&
                    (marker.transform.position - center).sqrMagnitude <= squaredRadius)
                    Undo.DestroyObjectImmediate(marker.gameObject);
            }

            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
        }

        private static bool TryGetSurfaceUnderMouse(Vector2 mousePosition, LayerMask mask, out RaycastHit hit)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            return TryRaycastPaintSurface(ray, Mathf.Infinity, mask, out hit);
        }

        private static bool TryRaycastPaintSurface(Ray ray, float distance, LayerMask mask, out RaycastHit hit)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, distance, mask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            foreach (RaycastHit candidate in hits)
            {
                if (candidate.collider.GetComponentInParent<PrefabScatterMarker>() != null)
                    continue;

                hit = candidate;
                return true;
            }

            hit = default;
            return false;
        }
    }
}
