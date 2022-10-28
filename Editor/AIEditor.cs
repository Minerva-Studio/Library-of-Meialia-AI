﻿using Amlos.AI;
using Minerva.Module;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Amlos.AI.Visual;

namespace Amlos.Editor
{

    public delegate void SelectNodeEvent(TreeNode node);
    public delegate void SelectServiceEvent(Service node);
    public delegate void NodeDrawerDelegate(TreeNode node);
    /// <summary>
    /// AI editor window
    /// </summary>
    public class AIEditor : EditorWindow
    {
        [Serializable]
        public class Setting
        {
            public float overviewWindowSize = 200;
            public int overviewHierachyIndentLevel = 5;
            public bool safeMode;
            internal bool useRawDrawer;
        }

        public enum Window
        {
            nodes,
            graph,
            variables,
            assetReference,
            properties,
            settings
        }
        public enum RightWindow
        {
            none,
            nodeType,
            determines,
            actions,
            calls,
            services,
            arithmetic,
        }

        public BehaviourTreeData tree;
        public Setting setting = new Setting();

        public int toolBarIndex;

        public Vector2 middleScrollPos;
        public Vector2 leftScrollPos;
        public Vector2 rightWindowScrollPos;


        public TreeNode selectedNode;
        public TreeNode selectedNodeParent;
        public Service selectedService;

        public NodeDrawHandler nodeDrawer;
        public SerializedProperty nodeRawDrawingProperty;

        public bool overviewWindowOpen = true;
        public Window window;
        public RightWindow rightWindow;
        public SelectNodeEvent selectEvent;

        public List<TreeNode> unreachables;
        public List<TreeNode> allNodes;
        private List<TreeNode> reachables;

        #region Graph 
        private List<GraphNode> graphNodes { get => tree?.Graph.graphNodes; set => tree.Graph.graphNodes = value; }
        private List<Connection> connections { get => tree?.Graph.connections; set => tree.Graph.connections = value; }


        private ConnectionPoint selectedInPoint;
        private ConnectionPoint selectedOutPoint;

        private Vector2 offset;
        private Vector2 drag;
        private SerializedObject obj;
        private EditorHeadNode editorHeadNode;

        #endregion

        public TreeNode SelectedNode { get => selectedNode; set { SelectNode(value); } }

        private void OnEnable()
        {
            obj ??= new SerializedObject(tree);
            obj.Update();
        }



        // Add menu item named "My Window" to the Window menu
        [MenuItem("Window/AI Editor")]
        public static AIEditor ShowWindow()
        {
            //Show existing window instance. If one doesn't exist, make one.
            var window = GetWindow(typeof(AIEditor), false, "AI Editor");
            window.name = "AI Editor";
            return window as AIEditor;

        }

        public void Load(BehaviourTreeData data)
        {
            tree = data;
            SelectedNode = data.Head;
        }

        private void SelectNode(TreeNode value)
        {
            rightWindow = RightWindow.none;
            selectedService = tree.IsServiceCall(value) ? tree.GetServiceHead(value) : null;
            selectedNode = value;
            selectedNodeParent = selectedNode != null ? tree.GetNode(selectedNode.parent) : null;
        }


        public void Refresh()
        {
            Initialize();
            SelectedNode = null;
        }

        private void Initialize()
        {
            setting ??= new Setting();
            if (tree) EditorUtility.SetDirty(tree);
        }

        void OnGUI()
        {
            Initialize();
            StartWindow();
            GUILayout.Space(5);

            GetAllNode();

            if (tree && window == Window.graph)
            {
                DrawGraph();
            }

            #region Draw Header
            GUILayout.Toolbar(-1, new string[] { "" });
            if (!SelectTree())
            {
                DrawNewBTWindow();
                EndWindow();
                return;
            }
            window = (Window)GUILayout.Toolbar((int)window, new string[] { "Tree", "Graph", "Variable Table", "Asset References", "Tree Properties", "Editor Settings" }, GUILayout.MinHeight(30));

            #endregion
            GUILayout.Space(10);

            //Initialize();
            GUI.enabled = !setting.safeMode;
            switch (window)
            {
                case Window.nodes:
                    DrawTree();
                    break;
                case Window.assetReference:
                    DrawAssetReferenceTable();
                    break;
                case Window.variables:
                    DrawVariableTable();
                    break;
                case Window.properties:
                    DrawProperties();
                    break;
                case Window.settings:
                    DrawSettings();
                    break;
                default:
                    break;
            }
            EndWindow();

            if (GUI.changed) Repaint();
        }

        #region Graph 

        private void DrawGraph()
        {
            DrawGrid(20, 0.2f, Color.gray);
            DrawGrid(100, 0.4f, Color.gray);

            DrawNodes();
            DrawConnections();

            DrawConnectionLine(Event.current);

            ProcessNodeEvents(Event.current);
            ProcessEvents(Event.current);
        }

        private void DrawGrid(float gridSpacing, float gridOpacity, Color gridColor)
        {
            int widthDivs = Mathf.CeilToInt(position.width / gridSpacing);
            int heightDivs = Mathf.CeilToInt(position.height / gridSpacing);

            Handles.BeginGUI();
            Handles.color = new Color(gridColor.r, gridColor.g, gridColor.b, gridOpacity);

            offset += drag * 0.5f;
            Vector3 newOffset = new Vector3(offset.x % gridSpacing, offset.y % gridSpacing, 0);

            for (int i = 0; i < widthDivs; i++)
            {
                Handles.DrawLine(new Vector3(gridSpacing * i, -gridSpacing, 0) + newOffset, new Vector3(gridSpacing * i, position.height, 0f) + newOffset);
            }

            for (int j = 0; j < heightDivs; j++)
            {
                Handles.DrawLine(new Vector3(-gridSpacing, gridSpacing * j, 0) + newOffset, new Vector3(position.width, gridSpacing * j, 0f) + newOffset);
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private void DrawNodes()
        {
            if (graphNodes != null)
            {
                for (int i = graphNodes.Count - 1; i >= 0; i--)
                {
                    GraphNode graphNode = graphNodes[i];
                    if (graphNode == null)
                    {
                        graphNodes.Remove(graphNode);
                        continue;
                    }
                    TreeNode child = tree.GetNode(graphNode.uuid);
                    if (child == null)
                    {
                        graphNodes.Remove(graphNode);
                        continue;
                    }
                    int index = 0;
                    string orderInfo;
                    TreeNodeType type;
                    if (child == tree.Head)
                    {
                        type = TreeNodeType.head;
                        orderInfo = "Head";
                        index = 0;
                    }
                    else
                    {
                        TreeNode parentNode = tree.GetNode(child.parent.uuid);
                        if (parentNode != null)
                        {
                            index = parentNode.GetIndexInfo(child);
                            orderInfo = parentNode.GetOrderInfo(child);
                        }
                        else
                        {
                            index = 0;
                            orderInfo = "";
                        }
                        type = unreachables.Contains(child) ? TreeNodeType.unused : TreeNodeType.@default;
                    }


                    graphNode.OnRemoveNode = OnClickRemoveNode;
                    graphNode.OnSelectNode = OnClickSelectNode;
                    graphNode.inPoint.OnClickConnectionPoint = OnClickInPoint;
                    graphNode.outPoint.OnClickConnectionPoint = OnClickOutPoint;
                    graphNode.Refresh(child, orderInfo, index, type);
                    graphNode.Draw();
                }
            }
        }

        private void DrawConnections()
        {
            if (connections != null)
            {
                for (int i = connections.Count - 1; i >= 0; i--)
                {
                    if (connections[i] == null)
                    {
                        connections.RemoveAt(i);
                        continue;
                    }
                    connections[i].OnClickRemoveConnection = OnClickRemoveConnection;
                    connections[i].Draw();
                }
            }
        }

        private void ProcessEvents(Event e)
        {
            drag = Vector2.zero;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        ClearConnectionSelection();
                    }

                    if (e.button == 1)
                    {
                        ProcessContextMenu(e.mousePosition);
                    }
                    break;

                case EventType.MouseDrag:
                    if (e.button == 0)
                    {
                        OnDrag(e.delta);
                    }
                    break;
            }
        }

        private void ProcessNodeEvents(Event e)
        {
            if (graphNodes != null)
            {
                for (int i = graphNodes.Count - 1; i >= 0; i--)
                {
                    bool guiChanged = graphNodes[i].ProcessEvents(e);

                    if (guiChanged)
                    {
                        GUI.changed = true;
                    }
                }
            }
        }

        private void DrawConnectionLine(Event e)
        {
            if (selectedInPoint != null && selectedOutPoint == null)
            {
                Handles.DrawBezier(
                    selectedInPoint.rect.center,
                    e.mousePosition,
                    selectedInPoint.rect.center + Vector2.left * 50f,
                    e.mousePosition - Vector2.left * 50f,
                    Color.white,
                    null,
                    2f
                );

                GUI.changed = true;
            }

            if (selectedOutPoint != null && selectedInPoint == null)
            {
                Handles.DrawBezier(
                    selectedOutPoint.rect.center,
                    e.mousePosition,
                    selectedOutPoint.rect.center - Vector2.left * 50f,
                    e.mousePosition + Vector2.left * 50f,
                    Color.white,
                    null,
                    2f
                );

                GUI.changed = true;
            }
        }

        private void ProcessContextMenu(Vector2 mousePosition)
        {
            GenericMenu genericMenu = new GenericMenu();
            genericMenu.AddItem(new GUIContent("Add node"), false, () => OnClickAddNode(mousePosition));
            genericMenu.ShowAsContext();
        }

        private void OnDrag(Vector2 delta)
        {
            drag = delta;

            if (graphNodes != null)
            {
                for (int i = 0; i < graphNodes.Count; i++)
                {
                    graphNodes[i].Drag(delta);
                }
            }

            GUI.changed = true;
        }

        private void OnClickAddNode(Vector2 mousePosition)
        {
            if (graphNodes == null)
            {
                graphNodes = new List<GraphNode>();
            }

            graphNodes.Add(new GraphNode(mousePosition, 200, 80));
        }

        private void OnClickInPoint(ConnectionPoint inPoint)
        {
            selectedInPoint = inPoint;

            if (selectedOutPoint != null)
            {
                if (selectedOutPoint.node != selectedInPoint.node)
                {
                    CreateConnection();
                    ClearConnectionSelection();
                }
                else
                {
                    ClearConnectionSelection();
                }
            }
        }

        private void OnClickOutPoint(ConnectionPoint outPoint)
        {
            selectedOutPoint = outPoint;

            if (selectedInPoint != null)
            {
                if (selectedOutPoint.node != selectedInPoint.node)
                {
                    CreateConnection();
                    ClearConnectionSelection();
                }
                else
                {
                    ClearConnectionSelection();
                }
            }
        }

        private void OnClickRemoveNode(GraphNode node)
        {
            if (connections != null)
            {
                List<Connection> connectionsToRemove = new List<Connection>();

                for (int i = 0; i < connections.Count; i++)
                {
                    if (connections[i].inPoint == node.inPoint || connections[i].outPoint == node.outPoint)
                    {
                        connectionsToRemove.Add(connections[i]);
                    }
                }

                for (int i = 0; i < connectionsToRemove.Count; i++)
                {
                    connections.Remove(connectionsToRemove[i]);
                }

                connectionsToRemove = null;
            }

            graphNodes.Remove(node);
        }

        private void OnClickSelectNode(GraphNode gnode)
        {
            TreeNode treeNode = tree.GetNode(gnode.uuid);
            SelectedNode = treeNode;
            //Debug.Log(treeNode);
            window = Window.nodes;
        }

        private void OnClickRemoveConnection(Connection connection)
        {
            connections.Remove(connection);
        }

        private void CreateConnection()
        {
            if (connections == null)
            {
                connections = new List<Connection>();
            }

            connections.Add(new Connection(selectedInPoint, selectedOutPoint, OnClickRemoveConnection));
        }

        private void ClearConnectionSelection()
        {
            selectedInPoint = null;
            selectedOutPoint = null;
        }


        /// <summary>
        /// Create the graph of this behaviour tree
        /// </summary>
        private void CreateGraph()
        {
            graphNodes ??= new List<GraphNode>();
            connections ??= new List<Connection>();
            graphNodes.Clear();
            connections.Clear();

            List<TreeNode> created = new();

            CreateGraph(tree.Head, Vector2.one * 200, created);
        }

        /// <summary>
        /// recursion of creating graph
        /// </summary>
        /// <param name="treeNode"></param>
        /// <param name="position"></param>
        /// <param name="created"></param>
        /// <param name="lvl"></param>
        /// <returns></returns>
        private GraphNode CreateGraph(TreeNode treeNode, Vector2 position, List<TreeNode> created, int lvl = 1)
        {
            GraphNode graphNode = new(position, 200, 80)
            {
                uuid = treeNode.uuid
            };
            graphNodes.Add(graphNode);
            created.Add(treeNode);
            List<NodeReference> list = treeNode.GetAllChildrenReference();
            Debug.Log(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                NodeReference item = list[i];

                TreeNode child = allNodes.FirstOrDefault(n => n.uuid == item.uuid);

                if (child == null) continue;
                if (created.Contains(child)) continue;

                var childPos = position + ((float)i / list.Count - 0.5f) * (2000f / lvl) * Vector2.right;
                childPos.y += 100;
                var node = CreateGraph(child, childPos, created, ++lvl);
                if (node != null) connections.Add(new Connection(node.inPoint, graphNode.outPoint, OnClickRemoveConnection));
            }
            return graphNode;
        }
        #endregion

        private void DrawNewBTWindow()
        {
            // Open Save panel and save it
            if (GUILayout.Button("Create New Behaviour Tree"))
            {
                var path = EditorUtility.SaveFilePanel("New Behaviour Tree", "", "AI_NewBehaviourTree.asset", "asset");
                if (path != "")
                {
                    var behaviourTree = CreateInstance<BehaviourTreeData>();
                    var p = Application.dataPath;
                    AssetDatabase.CreateAsset(behaviourTree, "Assets" + path[p.Length..path.Length]);
                    AssetDatabase.Refresh();
                    tree = behaviourTree;
                    window = Window.properties;


                    if (Selection.activeGameObject)
                    {
                        var aI = Selection.activeGameObject.GetComponent<AI.AI>();
                        if (!aI)
                        {
                            aI = Selection.activeGameObject.AddComponent<AI.AI>();
                        }
                        if (!aI.data)
                        {
                            aI.data = behaviourTree;
                        }
                    }
                }
            }
        }

        private bool SelectTree()
        {
            //if (!tree)
            //{
            //    allNodes = new List<TreeNode>();
            //    if (Selection.activeObject is BehaviourTreeData data) tree = data;
            //    else return false;
            //}
            var newTree = (BehaviourTreeData)EditorGUILayout.ObjectField("Behaviour Tree", tree, typeof(BehaviourTreeData), false);
            if (newTree != tree)
            {
                tree = newTree;
                if (newTree)
                {
                    EditorUtility.ClearDirty(tree);
                    EditorUtility.SetDirty(tree);
                    NewTreeSelectUpdate();
                    SelectedNode = tree.Head;
                }
                else
                {
                    tree = null;
                    return false;
                }
            }
            if (!tree)
            {
                tree = null;
                return false;
            }
            return true;
        }

        private void NewTreeSelectUpdate()
        {
            obj = new SerializedObject(tree);
            nodeRawDrawingProperty = null;
            GetAllNode();
        }

        private void DrawSettings()
        {
            GUILayout.BeginVertical();
            EditorGUILayout.LabelField("Settings");
            var currentStatus = GUI.enabled;
            GUI.enabled = true;
            GUILayout.Space(20);
            EditorGUILayout.LabelField("Overview");
            setting.overviewHierachyIndentLevel = EditorGUILayout.IntField("Hierachy Indent", setting.overviewHierachyIndentLevel);
            setting.overviewWindowSize = EditorGUILayout.FloatField("Window Size", setting.overviewWindowSize);
            GUILayout.Space(20);
            EditorGUILayout.LabelField("Other");
            bool v = false;// EditorGUILayout.Toggle("Use Raw Drawer", setting.useRawDrawer);
            if (v != setting.useRawDrawer)
            {
                setting.useRawDrawer = v;
                NewTreeSelectUpdate();
            }
            setting.safeMode = EditorGUILayout.Toggle("Safe Mode", setting.safeMode);
            GUILayout.Space(20);
            if (GUILayout.Button("Clear All Null Reference", GUILayout.Height(30), GUILayout.Width(200)))
                foreach (var node in allNodes) FillNullField(node);
            if (GUILayout.Button("Recreate Graph", GUILayout.Height(30), GUILayout.Width(200)))
                CreateGraph();
            //if (GUILayout.Button("Fix Connections", GUILayout.Height(30), GUILayout.Width(200)))
            //    FixConnections();
            if (GUILayout.Button("Reset Settings", GUILayout.Height(30), GUILayout.Width(200)))
                setting = new Setting();
            if (GUILayout.Button("Refresh Tree Window", GUILayout.Height(30), GUILayout.Width(200)))
            {
                tree.RegenerateTable();
                SelectedNode = tree.Head;

            }
            //if (GUILayout.Button("Reshadow"))
            //{
            //    Reshadow();
            //}
            GUI.enabled = currentStatus;
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private void FixConnections()
        {
            foreach (var parent in reachables)
            {
                foreach (var childRef in parent.GetAllChildrenReference())
                {
                    var child = tree.GetNode(childRef);
                    if (child != null)
                    {
                        child.parent = parent;
                    }
                }
            }
        }

        private void DrawAssetReferenceTable()
        {
            GUILayout.BeginVertical();
            EditorGUILayout.LabelField("Asset References");
            for (int i = 0; i < tree.assetReferences.Count; i++)
            {
                AssetReferenceData item = tree.assetReferences[i];
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("x", GUILayout.Width(20)))
                {
                    tree.assetReferences.Remove(item);
                    i--;
                    continue;
                }
                EditorGUILayout.LabelField(item.asset.name, GUILayout.Width(200));
                EditorGUILayout.ObjectField(tree.GetAsset(item.uuid), typeof(UnityEngine.Object), false);
                EditorGUILayout.LabelField(item.uuid);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(50);
            if (GUILayout.Button("Clear all unused asset"))
            {
                if (EditorUtility.DisplayDialog("Clear All Unused Asset", "Clear all unused asset?", "OK", "Cancel"))
                    tree.ClearUnusedAssetReference();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private void DrawProperties()
        {
            GUILayout.BeginVertical();
            EditorGUILayout.LabelField("Properties");
            var content = new GUIContent("Target Script", "the script that ai controls, usually an enemy script");
            tree.targetScript = EditorGUILayout.ObjectField(content, tree.targetScript, typeof(MonoScript), false) as MonoScript;
            content = new GUIContent("Target Animation Controller", "the animation controller of the AI");
            tree.animatorController = EditorGUILayout.ObjectField(content, tree.animatorController, typeof(UnityEditor.Animations.AnimatorController), false) as UnityEditor.Animations.AnimatorController;
            tree.errorHandle = (BehaviourTreeErrorSolution)EditorGUILayout.EnumPopup("Error Handle", tree.errorHandle);
            tree.noActionMaximumDurationLimit = EditorGUILayout.Toggle("Disable Action Time Limit", tree.noActionMaximumDurationLimit);
            if (!tree.noActionMaximumDurationLimit) tree.actionMaximumDuration = EditorGUILayout.FloatField("Maximum Execution Time", tree.actionMaximumDuration);
            GUILayout.EndVertical();
        }

        private void DrawVariableTable()
        {
            GUILayout.BeginVertical();
            var width = GUILayout.MaxWidth(150);
            EditorGUILayout.LabelField("Variable Table");
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("", GUILayout.MaxWidth(18));
            EditorGUILayout.LabelField("Info", width);
            //EditorGUILayout.LabelField("", width);
            EditorGUILayout.LabelField("Type", width);
            EditorGUILayout.LabelField("Name", width);
            EditorGUILayout.LabelField("Default", width);
            GUILayout.EndHorizontal();
            for (int index = 0; index < tree.variables.Count; index++)
            {
                VariableData item = tree.variables[index];
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("x", GUILayout.MaxWidth(18)))
                {
                    tree.variables.RemoveAt(index);
                    index--;
                    GUILayout.EndHorizontal();
                    continue;
                }
                EditorGUILayout.LabelField(item.type + ": " + item.name, width);
                item.type = (VariableType)EditorGUILayout.EnumPopup(item.type, width);
                item.name = EditorGUILayout.TextField(item.name, width);
                switch (item.type)
                {
                    case VariableType.String:
                        item.defaultValue = EditorGUILayout.TextField(item.defaultValue);
                        break;
                    case VariableType.Int:
                        {
                            bool i = int.TryParse(item.defaultValue, out int val);
                            if (!i) { val = 0; }
                            item.defaultValue = EditorGUILayout.IntField(val).ToString();
                        }
                        break;
                    case VariableType.Float:
                        {
                            bool i = float.TryParse(item.defaultValue, out float val);
                            if (!i) { val = 0; }
                            item.defaultValue = EditorGUILayout.FloatField(val).ToString();
                        }
                        break;
                    case VariableType.Bool:
                        {
                            bool i = bool.TryParse(item.defaultValue, out bool val);
                            if (!i) { val = false; }
                            item.defaultValue = EditorGUILayout.Toggle(val).ToString();
                        }
                        break;
                    case VariableType.Vector2:
                        {
                            bool i = VectorUtilities.TryParseVector2(item.defaultValue, out Vector2 val);
                            if (!i) { val = default; }
                            item.defaultValue = EditorGUILayout.Vector2Field("", val).ToString();
                        }
                        break;
                    case VariableType.Vector3:
                        {
                            bool i = VectorUtilities.TryParseVector3(item.defaultValue, out Vector3 val);
                            if (!i) { val = default; }
                            item.defaultValue = EditorGUILayout.Vector3Field("", val).ToString();
                        }
                        break;
                    default:
                        EditorGUILayout.LabelField("Invalid Variable Type");
                        break;
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (tree.variables.Count == 0)
            {
                EditorGUILayout.LabelField("No variable exist");
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add")) tree.variables.Add(new VariableData(tree.GenerateNewVariableName("newVar"), defaultValue: "default"));
            if (tree.variables.Count > 0 && GUILayout.Button("Remove")) tree.variables.RemoveAt(tree.variables.Count - 1);
            GUILayout.Space(50);
            GUILayout.EndVertical();


        }

        private void DrawTree()
        {
            if (!overviewWindowOpen) overviewWindowOpen = GUILayout.Button("Open Overview");
            GUILayout.BeginHorizontal();

            if (overviewWindowOpen) DrawOverview();

            GUILayout.Space(10);


            if (SelectedNode is null)
            {
                TreeNode head = tree.Head;
                if (head != null) SelectedNode = head;
                else CreateHeadNode();
            }
            if (SelectedNode != null)
            {
                if (SelectedNode is EditorHeadNode)
                {
                    DrawTreeHead();
                }
                else DrawSelectedNode(SelectedNode);
            }

            GUILayout.Space(10);

            if (rightWindow != RightWindow.none) DrawNodeTypeSelectionWindow();
            else DrawNodeTypeSelectionPlaceHolderWindow();

            GUILayout.EndHorizontal();
        }

        private void DrawTreeHead()
        {
            SelectNodeEvent selectEvent = (n) =>
            {
                tree.headNodeUUID = n?.uuid ?? UUID.Empty;
            };
            TreeNode head = tree.Head;
            string nodeName = head?.name ?? string.Empty;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Head: " + nodeName);
            EditorGUI.indentLevel++;
            if (head is null)
            {
                if (GUILayout.Button("Select.."))
                    OpenSelectionWindow(RightWindow.nodeType, selectEvent);
            }
            else
            {
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.BeginHorizontal(GUILayout.MaxWidth(80));
                GUILayout.Space(EditorGUI.indentLevel * 16);
                GUILayout.BeginVertical(GUILayout.MaxWidth(80));
                if (GUILayout.Button("Open"))
                {
                    Debug.Log("Open");
                    SelectedNode = head;
                }
                else if (GUILayout.Button("Replace"))
                {
                    OpenSelectionWindow(RightWindow.nodeType, selectEvent);
                }
                else if (GUILayout.Button("Delete"))
                {
                    tree.headNodeUUID = UUID.Empty;
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                var oldIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 1;
                GUILayout.BeginVertical();
                var currentStatus = GUI.enabled;
                GUI.enabled = false;
                var script = UnityEngine.Resources.FindObjectsOfTypeAll<MonoScript>().FirstOrDefault(n => n.GetClass() == head.GetType());
                EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
                GUI.enabled = currentStatus;

                head.name = EditorGUILayout.TextField("Name", head.name);
                EditorGUILayout.LabelField("UUID", head.uuid);

                GUILayout.EndVertical();
                EditorGUI.indentLevel = oldIndent;
            }
            EditorGUI.indentLevel--;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void GetAllNode()
        {
            if (!tree)
            {
                return;
            }
            allNodes = tree.AllNodes;
            reachables = GetReachableNodes();
            unreachables = allNodes.Except(reachables).ToList();
        }

        private List<TreeNode> GetReachableNodes()
        {
            List<TreeNode> nodes = new List<TreeNode>();
            if (tree.Head != null) GetReachableNodes(nodes, tree.Head);
            return nodes;
        }

        private void GetReachableNodes(List<TreeNode> list, TreeNode curr)
        {
            list.Add(curr);
            foreach (var item in curr.GetAllChildrenReference())
            {
                var node = tree.GetNode(item);
                if (node is not null && !list.Contains(node))
                {
                    GetReachableNodes(list, node);
                }
            }
        }

        /// <summary>
        /// Draw Overview window
        /// </summary>
        private void DrawOverview()
        {
            GUILayout.BeginVertical(GUILayout.MaxWidth(setting.overviewWindowSize), GUILayout.MinWidth(setting.overviewWindowSize - 1));

            EditorGUILayout.LabelField("Tree Overview");
            GUILayout.Space(10);
            leftScrollPos = EditorGUILayout.BeginScrollView(leftScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.MaxWidth(setting.overviewWindowSize - 50), GUILayout.MinWidth(setting.overviewWindowSize - 50), GUILayout.MinHeight(400));

            EditorGUILayout.LabelField("From Head");
            List<TreeNode> allNodeFromHead = new List<TreeNode>();

            if (tree.Head != null)
                if (GUILayout.Button("START"))
                {
                    editorHeadNode ??= new EditorHeadNode();
                    SelectedNode = editorHeadNode;
                }
            DrawOverview(tree.Head, allNodeFromHead, 0);

            GUILayout.Space(10);
            if (unreachables.Count() > 0)
            {
                EditorGUILayout.LabelField("Unreachable Nodes");
                foreach (var node in unreachables)
                {
                    if (GUILayout.Button(node.name))
                    {
                        SelectedNode = node;
                        GUILayout.EndVertical();
                        GUILayout.EndScrollView();
                        GUILayout.EndVertical();
                        return;
                    }
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.Space(10);
            overviewWindowOpen = !GUILayout.Button("Close");
            GUILayout.EndVertical();
        }

        /// <summary>
        /// helper for drawing overview
        /// </summary>
        /// <param name="node"></param>
        /// <param name="drawn"></param>
        /// <param name="indent"></param>
        private void DrawOverview(TreeNode node, List<TreeNode> drawn, int indent)
        {
            if (node == null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            if (GUILayout.Button(node.name))
            {
                SelectedNode = node;
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.EndHorizontal();
            drawn.Add(node);
            var children = node.services.Select(s => s.uuid).Union(node.GetAllChildrenReference().Select(r => r.uuid));
            if (children is null) return;

            foreach (var item in children)
            {
                TreeNode childNode = tree.GetNode(item);
                if (childNode is null) continue;
                if (drawn.Contains(childNode)) continue;
                if (childNode is Service) continue;
                //childNode.parent = node.uuid;
                DrawOverview(childNode, drawn, indent + setting.overviewHierachyIndentLevel);
                drawn.Add(childNode);
            }
        }

        /// <summary>
        /// helper for createing new head when the Ai file just created
        /// </summary>
        private void CreateHeadNode()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("No Head Node", EditorStyles.boldLabel);
            if (GUILayout.Button("Create", GUILayout.Height(30), GUILayout.Width(200)))
            {
                OpenSelectionWindow(RightWindow.nodeType, (node) =>
                 {
                     SelectedNode = node;
                     tree.headNodeUUID = SelectedNode.uuid;
                 });
            }
            GUILayout.EndVertical();
        }



        #region Right window

        /// <summary>
        /// draw node selection window (right)
        /// </summary>
        private void DrawNodeTypeSelectionWindow()
        {
            GUILayout.BeginVertical(GUILayout.Width(200));
            rightWindowScrollPos = GUILayout.BeginScrollView(rightWindowScrollPos);
            switch (rightWindow)
            {
                case RightWindow.nodeType:
                    DrawNodeSelectionWindow();
                    break;
                case RightWindow.actions:
                    DrawTypeSelectionWindow(typeof(AI.Action), () => rightWindow = RightWindow.nodeType);
                    break;
                case RightWindow.determines:
                    DrawTypeSelectionWindow(typeof(DetermineBase), () => rightWindow = RightWindow.nodeType);
                    break;
                case RightWindow.calls:
                    DrawTypeSelectionWindow(typeof(Call), () => rightWindow = RightWindow.nodeType);
                    break;
                case RightWindow.arithmetic:
                    DrawTypeSelectionWindow(typeof(Arithmetic), () => rightWindow = RightWindow.nodeType);
                    break;
                case RightWindow.services:
                    DrawTypeSelectionWindow(typeof(Service), () => rightWindow = RightWindow.none);
                    break;
            }

            GUILayout.EndScrollView();
            GUILayout.Space(50);
            if (GUILayout.Button("Close"))
            {
                rightWindow = RightWindow.none;
                GUILayout.EndVertical();
                return;
            }
            GUILayout.Space(50);
            GUILayout.EndVertical();
        }

        /// <summary>
        /// draw node selection window
        /// </summary>
        private void DrawNodeSelectionWindow()
        {
            DrawExistNodeSelectionWindow(typeof(TreeNode));
            GUILayout.Space(16);
            DrawCreateNewNodeWindow();
        }

        private void DrawExistNodeSelectionWindow(Type type)
        {
            var nodes = tree.AllNodes.Where(n => n.GetType().IsSubclassOf(type) && n != tree.Head).OrderBy(n => n.name);
            if (nodes.Count() == 0) return;
            GUILayout.Label("Exist Nodes...");
            foreach (var node in nodes)
            {
                //not a valid type
                if (!node.GetType().IsSubclassOf(type)) continue;
                //head
                if (node == tree.Head) continue;
                //select for service but the node is not allowed to appear in a service
                if (selectedService != null && Attribute.GetCustomAttribute(node.GetType(), typeof(AllowServiceCallAttribute)) == null) continue;
                if (GUILayout.Button(node.name))
                {
                    TreeNode parent = tree.GetNode(node.parent);
                    if (parent == null)
                    {
                        if (selectEvent == null)
                        {
                            Debug.LogWarning("No event exist");
                        }
                        selectEvent?.Invoke(node);
                        rightWindow = RightWindow.none;
                    }
                    else if (EditorUtility.DisplayDialog($"Node has a parent already", $"This Node is connecting to {parent.name}, move {(SelectedNode != null ? "under" + SelectedNode.name : "")} ?", "OK", "Cancel"))
                    {
                        var originParent = tree.GetNode(node.parent);
                        if (originParent is not null)
                        {
                            RemoveFromParent(originParent, node);
                        }
                        if (selectEvent == null)
                        {
                            Debug.LogWarning("No event exist");
                        }
                        else
                        {
                            Debug.LogWarning("event exist");
                        }
                        selectEvent?.Invoke(node);
                        Debug.LogWarning(selectEvent);
                        rightWindow = RightWindow.none;
                    }
                }
            }
        }

        private void DrawCreateNewNodeWindow()
        {
            GUILayout.Label("New...");
            GUILayout.Label("Composites");
            if (SelectFlowNodeType(out Type value))
            {
                TreeNode node = CreateNode(value);
                selectEvent?.Invoke(node);
                rightWindow = RightWindow.none;
            }
            GUILayout.Label("Logics");
            rightWindow = !GUILayout.Button(new GUIContent("Determine...", "A type of nodes that return true/false by determine conditions given")) ? rightWindow : RightWindow.determines;
            rightWindow = !GUILayout.Button(new GUIContent("Arithmetic...", "A type of nodes that do basic algorithm")) ? rightWindow : RightWindow.arithmetic;
            GUILayout.Label("Calls");
            rightWindow = !GUILayout.Button(new GUIContent("Calls...", "A type of nodes that calls certain methods")) ? rightWindow : RightWindow.calls;
            if (selectedService == null)
            {
                GUILayout.Label("Actions");
                rightWindow = !GUILayout.Button(new GUIContent("Actions...", "A type of nodes that perform certain actions")) ? rightWindow : RightWindow.actions;
            }
        }

        /// <summary>
        /// remove the node from parent's reference
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="child"></param>
        private void RemoveFromParent(TreeNode parent, TreeNode child)
        {
            UUID uuid = child.uuid;
            var fields = parent.GetType().GetFields();
            foreach (var item in fields)
            {
                if (item.FieldType == typeof(NodeReference))
                {
                    NodeReference nodeReference = (NodeReference)item.GetValue(parent);
                    if (nodeReference.uuid == uuid)
                        nodeReference.uuid = UUID.Empty;
                }
                else if (item.FieldType == typeof(List<Probability.EventWeight>))
                {
                    List<Probability.EventWeight> nodeReferences = (List<Probability.EventWeight>)item.GetValue(parent);
                    nodeReferences.RemoveAll(r => r.reference.uuid == uuid);
                }
                else if (item.FieldType == typeof(List<NodeReference>))
                {
                    List<NodeReference> nodeReferences = (List<NodeReference>)item.GetValue(parent);
                    nodeReferences.RemoveAll(r => r.uuid == uuid);
                }
                else if (item.FieldType == typeof(UUID))
                {
                    if ((UUID)item.GetValue(parent) == uuid)
                        item.SetValue(parent, UUID.Empty);
                }
            }
        }

        private void DrawTypeSelectionWindow(Type masterType, System.Action typeWindowCloseFunc)
        {
            var assembly = typeof(TreeNode).Assembly;
            var classes = assembly.GetTypes().Where(t => t.IsSubclassOf(masterType)).ToList();

            DrawExistNodeSelectionWindow(masterType);
            GUILayout.Label(masterType.Name);
            foreach (var type in classes)
            {
                if (type.IsAbstract) continue;
                if (Attribute.IsDefined(type, typeof(DoNotReleaseAttribute)))
                {
                    continue;
                }
                var content = new GUIContent(type.Name.ToTitleCase());
                if (Attribute.IsDefined(type, typeof(NodeTipAttribute)))
                {
                    content.tooltip = (Attribute.GetCustomAttribute(type, typeof(NodeTipAttribute)) as NodeTipAttribute).Tip;
                }
                if (GUILayout.Button(content))
                {
                    var n = CreateNode(type);
                    selectEvent?.Invoke(n);
                    typeWindowCloseFunc?.Invoke();
                    rightWindow = RightWindow.none;
                }
            }
            GUILayout.Space(16);
            if (GUILayout.Button("Back"))
            {
                typeWindowCloseFunc?.Invoke();
                return;
            }
        }


        private void DrawNodeTypeSelectionPlaceHolderWindow()
        {
            //var rect = GUILayoutUtility.GetRect(200 - 20, 1000);
            //EditorGUI.DrawRect(rect, Color.gray); 
            GUILayout.BeginVertical(GUILayout.Width(200));
            rightWindowScrollPos = EditorGUILayout.BeginScrollView(rightWindowScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);
            EditorGUILayout.LabelField("");
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        public void OpenSelectionWindow(RightWindow window, SelectNodeEvent e)
        {
            rightWindow = window;
            selectEvent = e;
            //Debug.Log("Set event");
        }
        #endregion





        private void SetMiddleWindowColorAndBeginVerticle()
        {
            var colorStyle = new GUIStyle();
            colorStyle.normal.background = Texture2D.whiteTexture;
            var baseColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(64 / 255f, 64 / 255f, 64 / 255f);
            GUILayout.BeginVertical(colorStyle, GUILayout.MinHeight(position.height - 130));
            GUI.backgroundColor = baseColor;
        }

        private void DrawSelectedNode(TreeNode node)
        {
            var currentGUIStatus = GUI.enabled;
            GUI.enabled = true;
            GUILayout.BeginVertical();
            middleScrollPos = GUILayout.BeginScrollView(middleScrollPos);
            GUI.enabled = currentGUIStatus;

            SetMiddleWindowColorAndBeginVerticle();
            if (unreachables != null && unreachables.Contains(node))
            {
                var textColor = GUI.contentColor;
                GUI.contentColor = Color.red;
                EditorGUILayout.LabelField("Warning: this node is unreachable");
                GUI.contentColor = textColor;
            }
            else if (selectedNodeParent == null) EditorGUILayout.LabelField("Tree Head");

            //if (setting.useRawDrawer)
            //{
            //    obj ??= new SerializedObject(tree);
            //    if (nodeRawDrawingProperty is null || (nodeRawDrawingProperty.GetValue() as TreeNode) != node)
            //    {
            //        SerializedProperty property = obj.FindProperty("nodes");
            //        if (property != null)
            //        {
            //            bool found = false;
            //            Debug.Log(property.arraySize);
            //            for (int i = 0; i < property.arraySize; i++)
            //            {
            //                if ((property.GetArrayElementAtIndex(i).GetValue() as TreeNode).uuid == node.uuid)
            //                {
            //                    nodeRawDrawingProperty = property.GetArrayElementAtIndex(i);
            //                    found = true;
            //                    break;
            //                }
            //            }
            //            if (!found)
            //            {
            //                Debug.Log("Cannot found property of node current");
            //            }
            //        }
            //    }

            //    if (nodeRawDrawingProperty == null) EditorGUILayout.LabelField("failed to draw item by raw drawer");
            //    else EditorGUILayout.PropertyField(nodeRawDrawingProperty, true);
            //    obj.UpdateIfRequiredOrScript();
            //    obj.ApplyModifiedProperties();
            //    obj.Update();
            //}
            //else
            {
                if (obj != null)
                {
                    obj.Dispose();
                    obj = null;
                }
                if (nodeDrawer == null || nodeDrawer.Node != node)
                    nodeDrawer = new(this, node);
                nodeDrawer.Draw();
            }



            if (!tree.IsServiceCall(node)) DrawNodeService(node);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            var option = GUILayout.Toolbar(-1, new string[] { "Open Parent", "", "Delete Node" }, GUILayout.MinHeight(30));
            if (option == 0)
            {
                SelectedNode = selectedNodeParent;
            }
            if (option == 2)
            {
                if (EditorUtility.DisplayDialog("Deleting Node", $"Delete the node {node.name} ({node.uuid}) ?", "OK", "Cancel"))
                {
                    var parent = tree.GetNode(node.parent);
                    tree.RemoveNode(node);
                    if (parent != null)
                    {
                        RemoveFromParent(parent, node);
                        SelectedNode = parent;
                    }
                    SelectedNode = tree.Head;
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawNodeService(TreeNode treeNode)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Service");
            if (treeNode.services == null)
            {
                treeNode.services = new List<NodeReference>();
            }
            if (treeNode.services.Count == 0)
            {
                EditorGUILayout.LabelField("No service");
            }
            else
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < treeNode.services.Count; i++)
                {
                    Service item = tree.GetNode(treeNode.services[i]) as Service;
                    if (item is null)
                    {
                        var currentColor = GUI.contentColor;
                        GUI.contentColor = Color.red;
                        GUILayout.Label("Node not found: " + treeNode.services[i]);
                        GUI.contentColor = currentColor;
                        continue;
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(18);
                    if (GUILayout.Button("x", GUILayout.MaxWidth(18)))
                    {
                        treeNode.services.RemoveAt(i);
                        item.parent = NodeReference.Empty;
                        if (EditorUtility.DisplayDialog("Delete Service", "Do you want to delete the service from the tree too?", "OK", "Cancel"))
                        {
                            tree.RemoveNode(item);
                        }
                    }
                    var formerGUIStatus = GUI.enabled;
                    if (i == 0) GUI.enabled = false;
                    if (GUILayout.Button("^", GUILayout.MaxWidth(18)))
                    {
                        treeNode.services.RemoveAt(i);
                        treeNode.services.Insert(i - 1, item);
                    }
                    GUI.enabled = formerGUIStatus;
                    if (i == treeNode.services.Count - 1) GUI.enabled = false;
                    if (GUILayout.Button("v", GUILayout.MaxWidth(18)))
                    {
                        treeNode.services.RemoveAt(i);
                        treeNode.services.Insert(i + 1, item);
                    }
                    GUI.enabled = formerGUIStatus;
                    EditorGUILayout.LabelField(item.GetType().Name);
                    if (GUILayout.Button("Open"))
                    {
                        SelectedNode = item;
                    }
                    GUILayout.EndHorizontal();
                }

                EditorGUI.indentLevel--;
            }
            if (GUILayout.Button("Add"))
            {
                OpenSelectionWindow(RightWindow.services, (e) =>
                {
                    treeNode.AddService(e as Service);
                    e.parent = treeNode;
                });
            }
            GUILayout.EndVertical();
        }




        public bool SelectFlowNodeType(out Type nodeType)
        {
            var types = typeof(Flow).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Flow)));
            foreach (var item in types)
            {
                if (GUILayout.Button(new GUIContent(item.Name.ToTitleCase(), NodeTypeExtension.GetTip(item.Name))))
                {
                    nodeType = item;
                    return true;
                }
            }
            nodeType = null;
            return false;
        }

        public TreeNode CreateNode(NodeType nodeType)
        {
            TreeNode node = null;
            switch (nodeType)
            {
                case NodeType.decision:
                    node = new Decision();
                    break;
                case NodeType.loop:
                    node = new Loop();
                    break;
                case NodeType.sequence:
                    node = new Sequence();
                    break;
                case NodeType.condition:
                    node = new Condition();
                    break;
                case NodeType.probability:
                    node = new Probability();
                    break;
                case NodeType.always:
                    node = new Always();
                    break;
                case NodeType.inverter:
                    node = new Inverter();
                    break;
                default:
                    break;
            }
            node.name = "New " + node.GetType().Name;
            tree.AddNode(node);
            return node;
        }

        public TreeNode CreateNode(Type nodeType)
        {
            if (nodeType.IsSubclassOf(typeof(TreeNode)))
            {
                TreeNode node = (TreeNode)Activator.CreateInstance(nodeType);
                tree.AddNode(node);
                node.name = tree.GenerateNewNodeName(node);
                FillNullField(node);
                return node;
            }
            throw new ArgumentException($"Type {nodeType} is not a valid type of node");
        }






        private void StartWindow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
        }

        private void EndWindow()
        {
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.enabled = true;
        }





        public override void SaveChanges()
        {
            AssetDatabase.SaveAssetIfDirty(tree);
            base.SaveChanges();
        }

        private void FillNullField(TreeNode node)
        {
            var type = node.GetType();
            var fields = type.GetFields();
            foreach (var field in fields)
            {
                var fieldType = field.FieldType;
                //Null Determine
                if (fieldType.IsClass && field.GetValue(node) is null)
                {
                    try
                    {
                        field.SetValue(node, Activator.CreateInstance(fieldType));
                    }
                    catch (Exception)
                    {
                        field.SetValue(node, default);
                        Debug.LogWarning("Field " + field.Name + " has not initialized yet. Provide this information if there are bugs");
                    }
                }

            }
        }

        /// <summary>
        /// A node that only use as a placeholder for AIE
        /// </summary>
        internal class EditorHeadNode : TreeNode
        {
            public NodeReference head = new NodeReference();

            public override void Execute()
            {
                throw new NotImplementedException();
            }

            public override void Initialize()
            {
                throw new NotImplementedException();
            }
        }
    }
}