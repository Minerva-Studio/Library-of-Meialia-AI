using Minerva.Module;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static UnityEditor.Progress;

namespace Amlos.AI
{
    /// <summary>
    /// The behaviour tree class that runs the behaviour tree
    /// </summary>
    /// <remarks>
    /// Author: Wendell
    /// </remarks>
    [Serializable]
    public partial class BehaviourTree
    {
        public delegate void UpdateDelegate();

        private static VariableTable globalVariables;
        private static Dictionary<BehaviourTreeData, VariableTable> staticVariablesDictionary = new();


        private const float defaultActionMaximumDuration = 60f;

        internal event UpdateDelegate UpdateCall;
        internal event UpdateDelegate LateUpdateCall;
        internal event UpdateDelegate FixedUpdateCall;


        [SerializeField] private bool isRunning;
        [SerializeField] private bool debug = false;
        [SerializeField] private bool pauseAfterSingleExecution = false;
        private readonly TreeNode head;
        private readonly Dictionary<UUID, TreeNode> references;
        private readonly VariableTable variables;
        private readonly VariableTable staticVariables;
        private readonly MonoBehaviour script;
        private readonly Dictionary<Service, ServiceStack> serviceStacks;
        private readonly float stageMaximumDuration;
        private NodeCallStack mainStack;
        private float currentStageDuration;
        private GameObject attachedGameObject;

        /// <summary> How long is current stage? </summary>
        public float CurrentStageDuration => currentStageDuration;
        public bool IsRunning { get => isRunning; set { isRunning = value; Log(isRunning); } }
        public bool IsPaused => IsRunning && (mainStack?.IsPaused == true);
        public TreeNode Head => head;
        public MonoBehaviour Script => script;
        public GameObject gameObject => attachedGameObject;
        public Dictionary<UUID, TreeNode> References => references;
        public VariableTable Variables => variables;
        public VariableTable StaticVariables => staticVariables;
        public BehaviourTreeData Prototype { get; private set; }
        public NodeCallStack MainStack => mainStack;
        public Dictionary<Service, ServiceStack> ServiceStacks => serviceStacks;
        public TreeNode CurrentStage => mainStack?.Current;
        public TreeNode LastStage => mainStack?.Last;

        private bool CanContinue => IsRunning && (mainStack?.IsPaused == false);
        public bool PauseAfterSingleExecution { get => pauseAfterSingleExecution; set => pauseAfterSingleExecution = value; }

        /// <summary>
        /// Global variables of the behaviour tree
        /// <br></br>
        /// (The variable shared in all behaviour tree)
        /// </summary>
        public static VariableTable GlobalVariables => globalVariables ??= InitGlobalVariable();





        public BehaviourTree(BehaviourTreeData behaviourTreeData, MonoBehaviour script) : this(behaviourTreeData, script.gameObject)
        {
            this.script = script;
        }

        public BehaviourTree(BehaviourTreeData behaviourTreeData, GameObject gameObject)
        {
            Prototype = behaviourTreeData;
            references = new Dictionary<UUID, TreeNode>();
            serviceStacks = new Dictionary<Service, ServiceStack>();

            variables = new VariableTable();
            staticVariables = GetStaticVariableTable();

            this.attachedGameObject = gameObject;

            GenerateReferenceTable();

            head = References[behaviourTreeData.headNodeUUID];
            if (head is null) { throw new InvalidBehaviourTreeException("Invalid behaviour tree, no head was found"); }

            if (!Prototype.noActionMaximumDurationLimit)
            {
                stageMaximumDuration = behaviourTreeData.actionMaximumDuration;
                if (stageMaximumDuration == 0) stageMaximumDuration = defaultActionMaximumDuration;
            }
            AssembleReference();
        }





        /// <summary>
        /// start execute behaviour tree
        /// </summary>
        public void Start()
        {
            try
            {
                Start_Internal();
            }
            catch (Exception)
            {
                IsRunning = false;
                throw;
            }
        }

        private void Start_Internal()
        {
            IsRunning = true;
            mainStack = new NodeCallStack();
            serviceStacks.Clear();

            mainStack.PauseAfterSingleExecution = PauseAfterSingleExecution;
            mainStack.Initialize();
            RegistryServices(head);
            ResetStageTimer();
            mainStack.Start(head);
        }

        /// <summary>
        /// let parent receive result
        /// </summary>
        /// <param name="node"></param>
        /// <param name="return"></param>
        public void ReceiveReturn(TreeNode node, bool @return)
        {
            NodeCallStack stack = GetStack(node);

            //trying to end other node
            if (stack.Current != node) return;
            //end the tree when the node is at the root
            if (!node.isInServiceRoutine) RemoveServicesRegistry(node);
            stack.ReceiveReturn(@return);
            if (stack.Count == 0) CleanUp();
        }




        /// <summary>
        /// add node to the progress stack
        /// </summary>
        /// <param name="node"></param>
        public void ExecuteNext(TreeNode node)
        {
            if (node is null)
            {
                Debug.LogException(new InvalidOperationException("Encounter null node"));
                switch (Prototype.errorHandle)
                {
                    case BehaviourTreeErrorSolution.Pause:
                        Pause();
                        break;
                    case BehaviourTreeErrorSolution.Restart:
                        Restart();
                        break;
                    case BehaviourTreeErrorSolution.Throw:
                        throw new InvalidBehaviourTreeException("Encounter null node in behaviour tree, behaviour tree paused");
                }
                return;
            }

            if (node.isInServiceRoutine)
            {
                ServiceStack stack = GetServiceStack(node.ServiceHead);
                stack.Push(node);
            }
            else
            {
                mainStack.Push(node);
                RegistryServices(node);
                ResetStageTimer();
            }
        }


        private NodeCallStack GetStack(TreeNode node)
        {
            return node.isInServiceRoutine ? serviceStacks[node.ServiceHead] : mainStack;
        }

        private ServiceStack GetServiceStack(Service node)
        {
            return serviceStacks[node.ServiceHead];
        }

        private void RegistryServices(TreeNode node)
        {
            foreach (var item in node.services)
            {
                Service service = item;
                serviceStacks[item] = new ServiceStack(service);
                service.OnRegistered();
            }
        }

        private void RemoveServicesRegistry(TreeNode node)
        {
            foreach (var item in node.services)
            {
                Service service = item;
                var stack = serviceStacks[service];
                stack.Break();
                serviceStacks.Remove(service);
                service.OnUnregistered();
            }
            ResetStageTimer();
        }

        private void RunService(ServiceStack serviceStack)
        {
            Service service = serviceStack.service;
            //last service hasn't finished 
            if (serviceStack.Count != 0)
            {
                Log($"Service {service.name} did not finish executing in expect time.");
                serviceStack.Break();
            }

            //execute
            serviceStack.Initialize();
            serviceStack.Start(service);
            //Debug.Log("Service Complete");
        }

        /// <summary>
        /// end a service
        /// </summary>
        /// <param name="service"></param>
        public void EndService(Service service)
        {
            var stack = GetServiceStack(service);
            if (stack == null) throw new ArgumentException("Given service does not exist", nameof(service));
            stack.End();
        }






        /// <summary>
        /// set behaviour tree wait for the node execution finished
        /// </summary>
        /// <param name="node"></param>
        public void Wait()
        {
            Log("Wait");
            mainStack.State = NodeCallStack.StackState.Waiting;
        }

        /// <summary>
        /// set behaviour tree wait for the node execution finished
        /// </summary>
        /// <param name="node"></param>
        public void WaitForNextFrame()
        {
            Log(mainStack.Current);
            mainStack.State = NodeCallStack.StackState.WaitUntilNextFrame;
        }

        public void Pause()
        {
            mainStack.IsPaused = true;
        }

        /// <summary>
        /// break the exist progress until the progress is at the given node <paramref name="stopAt"/>
        /// </summary>
        /// <param name="stopAt"></param>
        public void Break(TreeNode stopAt = null)
        {
            while (mainStack.Count > 0)
            {
                TreeNode treeNode = mainStack.Peek();
                if (treeNode == stopAt) break;
                mainStack.RollBack();
                RemoveServicesRegistry(treeNode);
            }

            //if no more node, the tree is ended
            if (mainStack.Count == 0) CleanUp();
            //else continue tree execution in the next frame
            else mainStack.State = NodeCallStack.StackState.Ready;
        }

        /// <summary>
        /// stop the tree
        /// </summary>
        public void End()
        {
            Break(null);
            CleanUp();
        }

        public void Resume()
        {
            if (mainStack.IsPaused) mainStack.IsPaused = false;
            mainStack.Continue();
        }

        /// <summary>
        /// restart the tree
        /// </summary>
        public void Restart()
        {
            Log("Restart");
            AssembleReference();
            mainStack.Initialize();
            RegistryServices(head);
            ResetStageTimer();
            mainStack.Start(head);
        }




        /// <summary>
        /// Update of behaviour tree, called every frame in the <see cref="AI"/>
        /// </summary>
        public void Update()
        {
            try
            {
                Update_Internal();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                switch (Prototype.errorHandle)
                {
                    case BehaviourTreeErrorSolution.Pause:
                        Pause();
                        break;
                    case BehaviourTreeErrorSolution.Restart:
                        Restart();
                        break;
                    case BehaviourTreeErrorSolution.Throw:
                        throw;
                }
                return;
            }
        }


        private void Update_Internal()
        {
            //don't update when paused
            if (mainStack.IsPaused)
            {
                return;
            }
            UpdateCall?.Invoke();
        }



        /// <summary>
        /// LateUpdate of behaviour tree, called every frame in the <see cref="AI"/>
        /// </summary>
        public void LateUpdate()
        {
            try
            {
                LateUpdate_Internal();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                switch (Prototype.errorHandle)
                {
                    case BehaviourTreeErrorSolution.Pause:
                        Pause();
                        break;
                    case BehaviourTreeErrorSolution.Restart:
                        Restart();
                        break;
                    case BehaviourTreeErrorSolution.Throw:
                        throw;
                }
                return;
            }
        }


        private void LateUpdate_Internal()
        {
            //don't update when paused
            if (mainStack.IsPaused)
            {
                return;
            }
            LateUpdateCall?.Invoke();
        }




        /// <summary>
        /// FixedUpdate of behaviour tree, called every frame in the <see cref="AI"/>
        /// </summary>
        public void FixedUpdate()
        {
            try
            {
                FixedUpdate_Internal();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                switch (Prototype.errorHandle)
                {
                    case BehaviourTreeErrorSolution.Pause:
                        Pause();
                        break;
                    case BehaviourTreeErrorSolution.Restart:
                        Restart();
                        break;
                    case BehaviourTreeErrorSolution.Throw:
                        throw;
                }
                return;
            }
        }

        private void FixedUpdate_Internal()
        {
            //don't update when paused
            if (mainStack.IsPaused)
            {
                return;
            }
            if (mainStack.State == NodeCallStack.StackState.WaitUntilNextFrame)
            {
                mainStack.State = NodeCallStack.StackState.Ready;
            }
            if (mainStack.State == NodeCallStack.StackState.Ready)
            {
                mainStack.Continue();
            }

            if (!CanContinue) return;
            FixedUpdateCall?.Invoke();
            if (!CanContinue) return;
            ServiceUpdate();
            if (!CanContinue) return;
            RunStageTimer();
        }




        /// <summary>
        /// Service update
        /// </summary>
        private void ServiceUpdate()
        {
            Log("Service Update Start :" + mainStack);
            var stack = mainStack.Nodes;
            for (int i = 0; i < mainStack.Count; i++)
            {
                var progress = stack[i];
                Log(progress.services.Count);
                for (int j = 0; j < progress.services.Count; j++)
                {
                    Service service = progress.services[j];

                    //service not found
                    if (!serviceStacks.TryGetValue(service, out var serviceStack))
                    {
                        Log($"Service {service.name} did not load into the behaviour tree properly.");
                        continue;
                    }
                    Log($"Service {service.name} Start");

                    //increase service timer
                    //serviceStack.currentFrame++;
                    service.UpdateTimer();
                    if (!service.IsReady) continue;

                    RunService(serviceStack);
                }
            }
        }




        /// <summary>
        /// Counter of the behaviour tree
        /// </summary>
        private void RunStageTimer()
        {
            currentStageDuration += Time.fixedDeltaTime;
            if (!Prototype.noActionMaximumDurationLimit && currentStageDuration >= stageMaximumDuration)
            {
                //abandon current progress, restart
                var lastCurrentStage = CurrentStage;
                Restart();
                Log("Behaviour Tree waiting for node " + lastCurrentStage.name + " too long. The tree has restarted");
            }
        }

        /// <summary>
        /// set current stage to this node
        /// </summary>
        /// <param name="treeNode"></param>
        private void ResetStageTimer()
        {
            currentStageDuration = 0;
        }




        /// <summary>
        /// Clean up behaviour tree execution
        /// </summary>
        private void CleanUp()
        {
            //Debug.Log("End");
            mainStack.Clear();
            IsRunning = false;
        }

        private void Log(object message)
        {
            if (debug) Debug.Log(message.ToString());
        }










        /// <summary>
        /// generate the reference table of the behaviour tree
        /// </summary>
        /// <param name="nodes"></param>
        /// <exception cref="InvalidBehaviourTreeException">if behaviour tree data is invalid</exception>
        private void GenerateReferenceTable()
        {
            IEnumerable<TreeNode> nodes = Prototype.GetNodesCopy();
            foreach (var node in nodes)
            {
                if (nodes is null)
                {
                    throw new InvalidBehaviourTreeException("A null node present in the behaviour tree, check your behaviour tree data.");
                }
                TreeNode newInstance = node.Clone();
                references[newInstance.uuid] = newInstance;
            }
            foreach (var item in Prototype.variables)
            {
                if (!item.isValid) continue;
                AddVariable(item);
            }
            foreach (var item in Prototype.assetReferences)
            {
                AddVariable(item);
            }
            //for node's null reference
            references[UUID.Empty] = null;
        }

        /// <summary>
        /// Assemble the reference UUID in the behaviour tree
        /// </summary>
        private void AssembleReference()
        {
            foreach (var node in references.Values)
            {
                if (node is null) continue;
                if (!references.ContainsKey(node.parent) && node != head) continue;
                node.behaviourTree = this;
                references.TryGetValue(node.parent, out var parent);
                node.parent = parent;
                node.services = node.services?.Select(u => (NodeReference)References[u]).ToList() ?? new List<NodeReference>();
                foreach (var service in node.services)
                {
                    TreeNode serviceNode = (TreeNode)service;
                    serviceNode.parent = references[serviceNode.parent];
                }
                FillAutoField(node);
                node.Initialize();
            }
        }

        /// <summary>
        /// Fill all auto field (field that should automatically filled before running behaviour tree)
        /// </summary>
        /// <param name="node">The tree node to be filled</param>
        private void FillAutoField(TreeNode node)
        {
            foreach (var field in node.GetType().GetFields())
            {
                if (field.FieldType.IsSubclassOf(typeof(VariableBase)))
                {
                    var reference = (VariableBase)field.GetValue(node);
                    VariableBase clone = (VariableBase)reference.Clone();

                    if (!clone.IsConstant) SetVariableFieldReference(clone);
                    else if (clone.Type == VariableType.UnityObject) SetVariableFieldReference(clone);

                    field.SetValue(node, clone);
                }
                else if (field.FieldType.IsSubclassOf(typeof(AssetReferenceBase)))
                {
                    var reference = (AssetReferenceBase)field.GetValue(node);
                    AssetReferenceBase clone = (AssetReferenceBase)reference.Clone();
                    clone.SetAsset(Prototype.GetAsset(reference.uuid));
                    field.SetValue(node, clone);
                }
                else if (field.FieldType.IsSubclassOf(typeof(NodeReference)))
                {
                    var reference = (NodeReference)field.GetValue(node);
                    NodeReference clone = reference.Clone();
                    field.SetValue(node, clone);
                }
                else if (field.FieldType.IsSubclassOf(typeof(RawNodeReference)))
                {
                    var reference = (RawNodeReference)field.GetValue(node);
                    RawNodeReference clone = reference.Clone();
                    field.SetValue(node, clone);
                }
            }
        }

        private VariableTable GetStaticVariableTable()
        {
            if (staticVariablesDictionary.TryGetValue(Prototype, out var table))
            {
                return table;
            }
            return staticVariablesDictionary[Prototype] = new VariableTable();
        }

        private void SetVariableFieldReference(VariableBase clone)
        {
            //try get field
            bool hasVar = variables.TryGetValue(clone.UUID, out Variable variable);
            if (!hasVar) hasVar = StaticVariables.TryGetValue(clone.UUID, out variable);
            if (!hasVar) hasVar = GlobalVariables.TryGetValue(clone.UUID, out variable);

            //get variable, if exist, then set reference to a variable, else set to null
            if (hasVar) clone.SetRuntimeReference(variable);
            else clone.SetRuntimeReference(null);
        }

        private Variable AddVariable(VariableData data)
        {
            if (!data.isStatic)
            {
                var localVar = new Variable(data);
                variables[data.UUID] = localVar;
                return localVar;
            }

            if (StaticVariables.TryGetValue(data.UUID, out var staticVar)) return staticVar;
            staticVar = new Variable(data, true);
            return StaticVariables[data.UUID] = staticVar;
        }

        private Variable AddVariable(AssetReferenceData data)
        {
            if (StaticVariables.TryGetValue(data.UUID, out var staticVar)) return staticVar;
            staticVar = new Variable(data);
            return StaticVariables[data.UUID] = staticVar;
        }

        public bool SetVariable(string name, object value)
        {
            if (Variables.TryGetValue(name, out var variable))
            {
                variable?.SetValue(value);
                return true;
            }
            return false;
        }





        private static VariableTable InitGlobalVariable()
        {
            var setting = AISetting.Instance;
            VariableTable globalVariables = new();
            foreach (var item in setting.globalVariables)
            {
                if (!item.isValid) continue;

                Variable variable = new(item, true);
                globalVariables[item.UUID] = variable;

                if (AIGlobalVariableInitAttribute.GetInitValue(item.name, out var value))
                {
                    variable.SetValue(value);
                }
            }
            return globalVariables;
        }

        public static bool SetGlobalVariable(string name, object value)
        {
            if (GlobalVariables.TryGetValue(name, out var variable))
            {
                variable?.SetValue(value);
                return true;
            }
            return false;
        }
    }
}
