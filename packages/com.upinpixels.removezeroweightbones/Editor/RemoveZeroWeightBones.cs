using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class RemoveZeroWeightBones : EditorWindow
{
    private GameObject armatureRoot;
    private bool keepTwistBones = true;
    private bool deleteEmptyParents = true;
    private List<string> logMessages = new List<string>();
    private Vector2 scrollPos;
    private bool showDetailedLog = false;
    private bool isProcessing = false;

    private int lastFoundBones = 0;
    private int lastRemovedBones = 0;
    private int lastErrorCount = 0;

    [MenuItem("Tools/UpInPixels/Remove Zero Weight Bones")]
    public static void ShowWindow()
    {
        GetWindow<RemoveZeroWeightBones>("Remove Zero Weight Bones");
    }

    private void OnGUI()
    {
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.fontSize = 14;
        EditorGUILayout.LabelField("Remove Zero Weight Bones", titleStyle);
        EditorGUILayout.LabelField("by UpInPixels", EditorStyles.miniLabel);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space(8);

        EditorGUILayout.HelpBox(
            "Deletes bone GameObjects that have zero weight on all meshes.\n" +
            "Does NOT modify mesh data – only the hierarchy is cleaned up.",
            MessageType.Info
        );

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Armature Settings", EditorStyles.boldLabel);
        armatureRoot = (GameObject)EditorGUILayout.ObjectField("Armature Root", armatureRoot, typeof(GameObject), true);

        // Warning if the selected object is not named "Armature" (case-insensitive)
        if (armatureRoot != null && !string.Equals(armatureRoot.name, "Armature", System.StringComparison.OrdinalIgnoreCase))
        {
            EditorGUILayout.HelpBox(
                $"The selected object is named '{armatureRoot.name}', not 'Armature'.\n" +
                "Make sure this is the correct armature root (the bone hierarchy).",
                MessageType.Warning
            );
        }

        EditorGUILayout.LabelField("Select the actual 'Armature' object (not the avatar root).", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

        keepTwistBones = EditorGUILayout.ToggleLeft("Keep Twist Bones", keepTwistBones);
        EditorGUILayout.LabelField("Bones with 'twist' in name or starting with '_'", EditorStyles.miniLabel);

        deleteEmptyParents = EditorGUILayout.ToggleLeft("Delete Empty Parent Bones", deleteEmptyParents);
        EditorGUILayout.LabelField("Deletes bones with components like PhysBone if all children are removed", EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        bool canRun = !isProcessing && armatureRoot != null;
        bool wasEnabled = GUI.enabled;
        GUI.enabled = canRun;

        string buttonText = isProcessing ? "Processing..." : "Remove Zero Weight Bones";
        if (GUILayout.Button(buttonText, GUILayout.Height(36)))
        {
            if (armatureRoot == null)
            {
                AddLog("Please assign an Armature Root.", true);
                return;
            }

            // Extra warning in dialog if name isn't "Armature"
            string nameWarning = "";
            if (!string.Equals(armatureRoot.name, "Armature", System.StringComparison.OrdinalIgnoreCase))
                nameWarning = $"WARNING: The selected object is named '{armatureRoot.name}', not 'Armature'.\nMake sure this is the correct armature root.\n\n";

            if (!EditorUtility.DisplayDialog("Remove Zero Weight Bones",
                nameWarning +
                $"This will delete all bone transforms under '{armatureRoot.name}' that have zero weight on every mesh.\n" +
                (keepTwistBones ? "• Twist bones will be kept.\n" : "• Twist bones will be deleted.\n") +
                (deleteEmptyParents ? "• Bones with components (PhysBone, etc.) will also be deleted if their children are gone.\n" : "• Only pure bones (Transform only) will be deleted.\n") +
                "\nMesh data is NOT modified.\n\nProceed?", "Yes", "Cancel"))
            {
                return;
            }

            logMessages.Clear();
            lastFoundBones = 0;
            lastRemovedBones = 0;
            lastErrorCount = 0;

            isProcessing = true;
            EditorApplication.delayCall += () => ProcessArmature(armatureRoot, keepTwistBones, deleteEmptyParents);
        }

        GUI.enabled = wasEnabled;

        EditorGUILayout.Space(8);

        if (lastFoundBones > 0 || lastRemovedBones > 0 || lastErrorCount > 0)
        {
            string summaryText = $"Found {lastFoundBones}   |   Removed {lastRemovedBones}   |   Errors {lastErrorCount}";
            MessageType msgType = lastErrorCount > 0 ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(summaryText, msgType);
        }
        else if (!isProcessing)
        {
            EditorGUILayout.HelpBox("Assign an Armature Root and press the button.", MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox("Processing...", MessageType.Info);
        }

        showDetailedLog = EditorGUILayout.Foldout(showDetailedLog, "Detailed Log", true, EditorStyles.foldoutHeader);
        if (showDetailedLog)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(180));

            string fullLog = logMessages.Count > 0 ? string.Join("\n", logMessages) : "No log entries.";
            GUIStyle readOnlyArea = new GUIStyle(EditorStyles.textArea);
            readOnlyArea.wordWrap = true;
            GUI.enabled = false;
            EditorGUILayout.TextArea(fullLog, readOnlyArea, GUILayout.ExpandHeight(true));
            GUI.enabled = true;

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
    }

    private void AddLog(string message, bool isError = false)
    {
        string prefix = isError ? "[Error] " : "";
        string fullMessage = prefix + message;
        logMessages.Add(fullMessage);
        if (isError) Debug.LogError("[RemoveZeroWeightBones] " + message);
        else Debug.Log("[RemoveZeroWeightBones] " + message);
    }

    private void ProcessArmature(GameObject root, bool keepTwist, bool deleteWithComponents)
    {
        int foundBones = 0;
        int removedBones = 0;
        int errorCount = 0;

        try
        {
            // Find all SkinnedMeshRenderers that reference any bone under this armature
            SkinnedMeshRenderer[] allSmrs = FindObjectsOfType<SkinnedMeshRenderer>(true);
            List<SkinnedMeshRenderer> relevantSmrs = new List<SkinnedMeshRenderer>();

            foreach (var smr in allSmrs)
            {
                if (smr.bones == null) continue;
                foreach (var bone in smr.bones)
                {
                    if (bone != null && bone.IsChildOf(root.transform))
                    {
                        relevantSmrs.Add(smr);
                        break;
                    }
                }
            }

            if (relevantSmrs.Count == 0)
            {
                AddLog("No SkinnedMeshRenderer found that references bones under the selected armature.", true);
                errorCount++;
                return;
            }

            AddLog($"Found {relevantSmrs.Count} SkinnedMeshRenderer(s) using this armature.");

            // Build a set of bones that have ANY vertex weight > 0
            HashSet<Transform> weightedBones = new HashSet<Transform>();

            foreach (var smr in relevantSmrs)
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null) continue;

                BoneWeight[] weights = mesh.boneWeights;
                if (weights == null || weights.Length == 0) continue;

                Transform[] bones = smr.bones;
                if (bones == null) continue;

                for (int v = 0; v < weights.Length; v++)
                {
                    BoneWeight bw = weights[v];
                    if (bw.weight0 > 0 && bw.boneIndex0 < bones.Length && bones[bw.boneIndex0] != null && bones[bw.boneIndex0].IsChildOf(root.transform))
                        weightedBones.Add(bones[bw.boneIndex0]);
                    if (bw.weight1 > 0 && bw.boneIndex1 < bones.Length && bones[bw.boneIndex1] != null && bones[bw.boneIndex1].IsChildOf(root.transform))
                        weightedBones.Add(bones[bw.boneIndex1]);
                    if (bw.weight2 > 0 && bw.boneIndex2 < bones.Length && bones[bw.boneIndex2] != null && bones[bw.boneIndex2].IsChildOf(root.transform))
                        weightedBones.Add(bones[bw.boneIndex2]);
                    if (bw.weight3 > 0 && bw.boneIndex3 < bones.Length && bones[bw.boneIndex3] != null && bones[bw.boneIndex3].IsChildOf(root.transform))
                        weightedBones.Add(bones[bw.boneIndex3]);
                }
            }

            // Build the "keep" set: root, weighted bones, and all their ancestors
            HashSet<Transform> keep = new HashSet<Transform>();
            keep.Add(root.transform);

            // Add weighted bones and their ancestors
            foreach (Transform t in weightedBones)
            {
                Transform current = t;
                while (current != null && current != root.transform)
                {
                    keep.Add(current);
                    current = current.parent;
                }
            }

            // Optionally keep twist bones and their ancestors
            if (keepTwist)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t == root.transform) continue;
                    string name = t.name.ToLower();
                    if (name.Contains("twist") || t.name.StartsWith("_"))
                    {
                        Transform current = t;
                        while (current != null && current != root.transform)
                        {
                            keep.Add(current);
                            current = current.parent;
                        }
                        AddLog($"Kept twist bone: {t.name}");
                    }
                }
            }

            // Now all transforms under root that are NOT in keep are candidates for deletion
            Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
            List<Transform> candidates = new List<Transform>();
            foreach (Transform t in allTransforms)
            {
                if (t == root.transform) continue;
                if (!keep.Contains(t))
                    candidates.Add(t);
            }

            foundBones = candidates.Count;
            AddLog($"Found {foundBones} zero‑weight bone(s) to delete.");

            if (foundBones == 0)
            {
                AddLog("No zero‑weight bones to delete.");
                return;
            }

            // Sort by depth descending (children first)
            candidates = candidates.OrderByDescending(t => GetDepth(t)).ToList();

            // Delete candidates
            foreach (Transform t in candidates)
            {
                // Safety: never delete objects with Renderer or Collider
                if (t.GetComponent<Renderer>() != null || t.GetComponent<Collider>() != null)
                {
                    AddLog($"Skipping '{t.name}' - has Renderer or Collider.", true);
                    errorCount++;
                    continue;
                }

                // If "deleteEmptyParents" is disabled, skip objects that have any component other than Transform
                if (!deleteWithComponents)
                {
                    Component[] components = t.GetComponents<Component>();
                    if (components.Length > 1) // more than just Transform
                    {
                        AddLog($"Skipping '{t.name}' - has additional components (disable this option to force delete).", true);
                        errorCount++;
                        continue;
                    }
                }

                string boneName = t.name;
                Undo.DestroyObjectImmediate(t.gameObject);
                removedBones++;
                AddLog($"Deleted zero‑weight bone: {boneName}");
            }

            AddLog($"Cleanup complete. Removed {removedBones} zero‑weight bone(s).");
        }
        catch (System.Exception e)
        {
            AddLog($"Unexpected error: {e.Message}\n{e.StackTrace}", true);
            errorCount++;
        }
        finally
        {
            lastFoundBones = foundBones;
            lastRemovedBones = removedBones;
            lastErrorCount = errorCount;
            isProcessing = false;
            Repaint();
        }
    }

    private int GetDepth(Transform t)
    {
        int depth = 0;
        while (t.parent != null)
        {
            depth++;
            t = t.parent;
        }
        return depth;
    }
}