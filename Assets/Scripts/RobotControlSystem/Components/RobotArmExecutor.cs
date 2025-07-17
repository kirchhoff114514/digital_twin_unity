// File: Assets/Scripts/RobotControlSystem/Components/RobotArmExecutor.cs
// Description: 负责执行机器人运动指令，驱动虚拟机械臂在Unity中进行显示同步。
//              在Awake时将当前手动设置的关节位置作为新的零位基准。

using UnityEngine;
using System; // For Debug.Log (string interpolation)
using System.Linq; // For Debug.Log (string.Join)

/// <summary>
/// 机械臂运动的显示执行者。
/// 接收关节角度轨迹指令，驱动虚拟机械臂平滑运动。
/// 将启动时的当前关节姿态作为后续运动的零位基准。
/// </summary>
public class RobotArmExecutor : MonoBehaviour
{
    // --- 公共引用（Inspector 或 RobotControlSystemManager 注入） ---

    /// <summary>
    /// 虚拟机械臂所有可动关节的 Transform 数组。请确保按关节顺序赋值。
    /// </summary>
    [Tooltip("拖拽所有虚拟机械臂关节的 Transform 组件到此数组中，按顺序排列。")]
    public Transform[] robotJointTransforms;

    /// <summary>
    /// 虚拟机械臂每个关节的局部旋转轴。
    /// 确保数组顺序与 robotJointTransforms 对应。
    /// 例如：Vector3.up (0,1,0) 代表绕局部Y轴，Vector3.right (1,0,0) 代表绕局部X轴。
    /// </summary>
    [Tooltip("指定每个关节的局部旋转轴。顺序应与Robot Joint Transforms数组一致。")]
    public Vector3[] jointRotationAxes;

    /// <summary>
    /// 虚拟机械臂关节的旋转速度（度/秒）。
    /// </summary>
    [Tooltip("虚拟机械臂关节的旋转速度（度/秒）。")]
    [Range(10f, 360f)]
    public float virtualArmRotationSpeed = 100f;

    /// <summary>
    /// 判断虚拟关节是否已到达当前轨迹点的角度容差（度）。
    /// </summary>
    [Tooltip("判断虚拟关节是否已到达目标角度的容差（度）。")]
    [Range(0.01f, 1.0f)]
    public float jointReachThreshold = 0.5f;

    /// <summary>
    /// MotionPlanner 实例的引用，用于在关节更新时通知其最新状态。
    /// 在 RobotControlSystemManager 中设置此引用。
    /// </summary>
    [Tooltip("拖拽 MotionPlanner 组件到此处，以便在执行器更新关节时通知规划器。")]
    public MotionPlanner motionPlanner;


    // --- 内部状态 ---
    private RobotMotionCommand _currentMotionCommand; // 当前执行的运动指令
    private int _currentWaypointIndex = 0;           // 当前轨迹点索引
    private bool _isExecutingTrajectory = false;      // 是否正在执行轨迹

    // 新增：用于存储每个关节在 Awake 时捕捉到的“自定义零位”旋转
    // 这是机械臂模型在 Unity Scene 中手动摆放的初始姿态
    private Quaternion[] _initialZeroRotations;

    // 用于存储每个关节在 Update 中计算出的最终目标旋转四元数
    // 这个目标旋转是在 _initialZeroRotations 基础上叠加 MotionPlanner 给出角度后的结果
    private Quaternion[] _targetJointRotations;

    // 用于保存当前关节的实际角度值（相对于机械臂的物理零位，与 MotionPlanner 交互）
    // 这个数组在每次执行轨迹点后会更新，并传递给 MotionPlanner
    private float[] _currentJointAngles; 


    // --- Unity 生命周期方法 ---

    void Awake()
    {
        // --- 初始化检查 ---
        if (robotJointTransforms == null || robotJointTransforms.Length == 0)
        {
            Debug.LogError("RobotArmExecutor: 'Robot Joint Transforms' 数组为空或未设置！无法启动。", this);
            enabled = false;
            return;
        }
        if (jointRotationAxes == null || jointRotationAxes.Length != robotJointTransforms.Length)
        {
            Debug.LogError("RobotArmExecutor: 'Joint Rotation Axes' 数组未设置或长度与关节数量不匹配！无法启动。", this);
            enabled = false;
            return;
        }
        if (motionPlanner == null)
        {
            Debug.LogError("RobotArmExecutor: MotionPlanner 引用未设置！无法通知其当前关节状态。", this);
            enabled = false;
            return;
        }

        // --- 记录当前（手动调整后）的关节姿态作为新的零位基准 ---
        _initialZeroRotations = new Quaternion[robotJointTransforms.Length];
        _targetJointRotations = new Quaternion[robotJointTransforms.Length];
        _currentJointAngles = new float[robotJointTransforms.Length]; // 初始化当前关节角度数组

        for (int i = 0; i < robotJointTransforms.Length; i++)
        {
            if (robotJointTransforms[i] != null)
            {
                // 记录每个关节在 Awake 时的局部旋转，这被视为该关节的“零位”或“初始校准位”
                _initialZeroRotations[i] = robotJointTransforms[i].localRotation;
                
                // 将当前关节的实际角度（相对于初始零位）设置为0，因为这是我们新的相对基准
                _currentJointAngles[i] = 0f; 

                Debug.Log($"RobotArmExecutor: 关节 {robotJointTransforms[i].name} 的初始零位已记录为: {_initialZeroRotations[i].eulerAngles}");
            }
            else
            {
                Debug.LogWarning($"RobotArmExecutor: 关节 {i} 的 Transform 为空，无法记录零位！");
            }
        }
        
        // 第一次通知 MotionPlanner 机械臂的初始状态
        motionPlanner.UpdateCurrentJointAngles(_currentJointAngles);
        Debug.Log("RobotArmExecutor: 初始化完成，当前关节姿态已记录为新的零位基准。等待 MotionPlanner 指令。");
    }

    void Update()
    {
        if (_isExecutingTrajectory && _currentMotionCommand.TrajectoryJointAngles != null)
        {
            if (_currentWaypointIndex < _currentMotionCommand.TrajectoryJointAngles.Length)
            {
                float[] currentWaypointTargetAngles = _currentMotionCommand.TrajectoryJointAngles[_currentWaypointIndex];

                if (currentWaypointTargetAngles.Length != robotJointTransforms.Length)
                {
                    Debug.LogError($"RobotArmExecutor: 轨迹点 {_currentWaypointIndex} 的关节数量 ({currentWaypointTargetAngles.Length}) 不匹配机械臂自由度 ({robotJointTransforms.Length})！停止虚拟臂同步。", this);
                    StopExecution();
                    return;
                }

                bool allJointsReachedCurrentWaypoint = true;

                for (int i = 0; i < robotJointTransforms.Length; i++)
                {
                    Transform joint = robotJointTransforms[i];
                    if (joint == null) continue; // 跳过空的关节引用

                    float motionPlannerAngle = currentWaypointTargetAngles[i]; // 这是来自 MotionPlanner 的角度，相对于物理零位
                    Vector3 rotationAxis = jointRotationAxes[i];             // 当前关节的局部旋转轴
                    
                    // --- 核心逻辑：将 MotionPlanner 的角度（相对于物理零位）叠加到 Unity 模型的自定义零位之上 ---
                    // 1. 创建一个代表 MotionPlanner 输出角度的旋转 (围绕其指定轴)
                    // Quaternion.AngleAxis(angle, axis) 返回一个围绕指定轴旋转指定角度的四元数
                    Quaternion angleFromMotionPlanner = Quaternion.AngleAxis(motionPlannerAngle, rotationAxis);

                    // 2. 将此旋转叠加到 Awake 时记录的“初始零位”上
                    // 注意：这里的乘法顺序很重要。_initialZeroRotations 是基准旋转，angleFromMotionPlanner 是在其基础上应用的“增量”旋转。
                    _targetJointRotations[i] = _initialZeroRotations[i] * angleFromMotionPlanner;

                    // 平滑旋转关节
                    joint.localRotation = Quaternion.RotateTowards(
                        joint.localRotation, // 当前旋转
                        _targetJointRotations[i], // 目标旋转
                        virtualArmRotationSpeed * Time.deltaTime // 旋转速度
                    );

                    // 判断是否到达目标角度（这里判断的是最终的 localRotation 是否接近目标）
                    if (Quaternion.Angle(joint.localRotation, _targetJointRotations[i]) > jointReachThreshold)
                    {
                        allJointsReachedCurrentWaypoint = false;
                    }

                    // 实时更新当前关节角度（相对于物理零位）
                    // 这一步是为了让 MotionPlanner 能够获取到执行器当前的真实关节角度，用于 IK 初始猜测等
                    // 但是，直接从 Quaternion.AngleAxis(rotationAxis, joint.localRotation * Quaternion.Inverse(_initialZeroRotations[i])).eulerAngles.magnitude; 
                    // 来反推角度比较复杂且可能不准确。
                    // 更简化的方式是：当关节足够接近目标时，我们认为它达到了当前轨迹点的角度。
                    // 也就是说，当 allJointsReachedCurrentWaypoint 为 true 时，_currentJointAngles 才会被更新为该轨迹点的目标角度。
                    // 否则，它仍然是上一个已达到的轨迹点角度。
                    // 如果需要更连续的实时反馈，可以考虑另一种反推方案或增加 Executor 的事件。
                }

                if (allJointsReachedCurrentWaypoint)
                {
                    // 当到达当前轨迹点后，更新内部的 _currentJointAngles 数组，
                    // 以便在下一次需要时（例如 MotionPlanner 调用 GetCurrentJointAngles()）能提供准确的值。
                    Array.Copy(currentWaypointTargetAngles, _currentJointAngles, _currentJointAngles.Length);
                    motionPlanner.UpdateCurrentJointAngles(_currentJointAngles); // 通知 MotionPlanner
                    Debug.Log($"RobotArmExecutor: 虚拟机械臂已到达轨迹点 {_currentWaypointIndex}。当前关节角度: {string.Join(", ", _currentJointAngles.Select(a => a.ToString("F1")))} (Command ID: {_currentMotionCommand.CommandID})。", this);
                    _currentWaypointIndex++;
                }
            }
            else // 所有轨迹点执行完毕
            {
                Debug.Log($"RobotArmExecutor: 虚拟机械臂轨迹同步完毕 (Command ID: {_currentMotionCommand.CommandID})。", this);
                StopExecution();
            }
        }
    }

    // --- 公共方法 ---

    /// <summary>
    /// 接收并开始在 Unity 中同步一个新的机器人运动指令。
    /// 这个方法由 MotionPlanner 发布 OnRobotMotionCommandReady 事件后触发。
    /// </summary>
    /// <param name="command">包含要执行的关节角度轨迹序列的 RobotMotionCommand。</param>
    public void ExecuteMotionCommand(RobotMotionCommand command) // 注意：这里方法名与 MotionPlanner 的委托匹配
    {
        if (command.TrajectoryJointAngles == null || command.TrajectoryJointAngles.Length == 0)
        {
            Debug.LogWarning($"RobotArmExecutor: 收到一个空的或无效的运动指令 (ID: {command.CommandID})。忽略并停止虚拟臂同步。", this);
            StopExecution();
            return;
        }

        StopExecution(); // 停止当前执行中的命令（如果存在），准备开始新命令
        _currentMotionCommand = command;
        _currentWaypointIndex = 0;
        _isExecutingTrajectory = true;
        Debug.Log($"RobotArmExecutor: 收到新运动指令 (ID: {_currentMotionCommand.CommandID})，包含 {_currentMotionCommand.TrajectoryJointAngles.Length} 个轨迹点。开始同步虚拟机械臂。", this);
    }

    // --- 私有辅助方法 ---

    /// <summary>
    /// 停止当前的虚拟机械臂轨迹执行，重置内部状态。
    /// </summary>
    private void StopExecution()
    {
        _isExecutingTrajectory = false;
        _currentWaypointIndex = 0;
        // 注意：这里 _currentMotionCommand 不设为 null，而是新建一个空实例，
        // 确保后续检查 _currentMotionCommand.TrajectoryJointAngles 不会引发空引用异常
        _currentMotionCommand = new RobotMotionCommand(); 
    }
}