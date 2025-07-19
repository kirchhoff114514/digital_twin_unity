// File: Assets/Scripts/RobotControlSystem/Components/MotionPlanner.cs
// Description: 机械臂的运动规划器，根据用户意图（RobotControlIntent）生成具体的运动指令（RobotMotionCommand）。

using UnityEngine;
using System; // For Action delegate and Tuple
using System.Collections.Generic; // For List<float[]>

/// <summary>
/// MotionPlanner 负责将高层级的用户控制意图（RobotControlIntent）
/// 转换为低层级的机械臂运动指令（RobotMotionCommand）。
/// 它处理逆运动学、路径规划和轨迹生成。
/// </summary>
public class MotionPlanner : MonoBehaviour
{
    // --- 事件定义 ---
    /// <summary>
    /// 当运动指令准备好被执行时触发此事件。
    /// RobotArmExecutor 和 SerialCommunicator 将订阅此事件。
    /// </summary>
    public event Action<RobotMotionCommand> OnRobotMotionCommandReadyForExecution;

    // --- 公共引用（Inspector 或 RobotControlSystemManager 注入） ---

    [Header("依赖组件")]
    [Tooltip("拖拽 KinematicsCalculator 组件到此处。")]
    public KinematicsCalculator kinematicsCalculator; // 引用新的 KinematicsCalculator
    [Tooltip("拖拽 PathPlanner 组件到此处。")]
    public PathPlanner pathPlanner; // 用于生成关节空间或笛卡尔空间的平滑轨迹

    [Header("规划参数")]
    [Tooltip("生成的轨迹点之间的最小时间间隔（秒）。影响轨迹的平滑度和点密度。")]
    [Range(0.01f, 0.5f)]
    public float trajectoryWaypointTimeStep = 0.05f; // 默认每 50ms 生成一个轨迹点
    public float totaltime = 1.0f; // 默认每 50ms 生成一个轨迹点

    [Tooltip("每个关节的自由度数量。例如，5轴机械臂就是5。")]
    public int robotDOF = 5; // 机械臂的自由度 (Degrees Of Freedom)

    // --- 内部状态 ---
    // 存储机械臂当前的关节角度。
    // IMPORTANT: 这个值应该由 RobotArmExecutor 或实际传感器更新，以保持同步。
    private float[] _currentJointAngles; 
    private int _commandCounter = 0;     // 用于生成唯一的 CommandID

    void Awake()
    {
        // 初始化检查：确保必要组件已设置
        if (kinematicsCalculator == null)
        {
            Debug.LogError("MotionPlanner: 'Kinematics Calculator' 引用未设置。请在 Inspector 中拖拽赋值。", this);
            enabled = false;
            return;
        }
        if (pathPlanner == null)
        {
            Debug.LogError("MotionPlanner: 'Path Planner' 引用未设置。请在 Inspector 中拖拽赋值。", this);
            enabled = false;
            return;
        }

        // 初始化当前关节角度数组。
        _currentJointAngles = new float[robotDOF]; 
        for (int i = 0; i < robotDOF; i++)
        {
            _currentJointAngles[i] = 0f; // 初始所有关节角度为 0
        }

        Debug.Log("MotionPlanner: 初始化完成。等待 RobotInputManager 的意图。");
    }

    /// <summary>
    /// 接收 RobotInputManager 发布的机器人控制意图。
    /// 这是 MotionPlanner 的主要入口点。
    /// </summary>
    /// <param name="intent">包含用户意图的 RobotControlIntent。</param>
    public void ProcessRobotControlIntent(RobotControlIntent intent)
    {
        Debug.Log($"MotionPlanner: 收到新的控制意图。模式: {intent.Mode}, ID: {intent.Timestamp}");
        _commandCounter++; // 为新命令生成唯一ID

        RobotMotionCommand command = new RobotMotionCommand();
        command.CommandID = _commandCounter;
        command.ExecutionSpeed = 1.0f; // 默认执行速度，可根据逻辑调整

        // 根据不同的控制模式执行规划逻辑
        switch (intent.Mode)
        {
            case ControlMode.JointSpaceTeaching:
                command = HandleJointSpaceTeaching(intent);
                break;
            case ControlMode.TaskControl:
                command = HandleTaskControl(intent);
                break;
            case ControlMode.Demonstration:
                command = HandleDemonstration(intent);
                break;
            default:
                Debug.LogWarning($"MotionPlanner: 未知的控制模式: {intent.Mode}。忽略此意图。", this);
                return;
        }

        // 如果成功生成了命令，则发布
        if (command.TrajectoryJointAngles != null && command.TrajectoryJointAngles.Length > 0)
        {
            OnRobotMotionCommandReadyForExecution?.Invoke(command);
            Debug.Log($"MotionPlanner: 已发布 RobotMotionCommand (ID: {command.CommandID}, 类型: {command.CommandType})，包含 {command.TrajectoryJointAngles.Length} 个轨迹点。");
        }
        else
        {
            Debug.LogWarning($"MotionPlanner: 未能为意图 (ID: {intent.Timestamp}) 生成有效的运动指令。", this);
        }
    }

    /// <summary>
    /// 处理示教模式的意图。
    /// 此时用户直接给出关节角度，无需复杂规划。
    /// </summary>
    private RobotMotionCommand HandleJointSpaceTeaching(RobotControlIntent intent)
    {
        // 示教模式通常是直接将当前关节角度作为目标，形成一个单点轨迹。
        // 实时更新时，每次滑块变动都会生成一个新命令，包含当前所有关节的角度。
        if (intent.JointAngles == null || intent.JointAngles.Length != robotDOF)
        {
            Debug.LogError($"MotionPlanner: 示教模式意图的关节数量不匹配 DOF ({robotDOF}) 或为空。", this);
            return new RobotMotionCommand();
        }

        // 更新内部记录的当前关节角度（模拟实时反馈）
        _currentJointAngles = (float[])intent.JointAngles.Clone(); // 克隆数组避免引用问题

        // 使用 RobotMotionCommand 的静态工厂方法创建命令
        RobotMotionCommand command = RobotMotionCommand.CreateSinglePointCommand(
            intent.JointAngles,
            _commandCounter,
            1.0f // 示教模式通常立即响应，速度因子为1
        );
        return command;
    }

    /// <summary>
    /// 处理控制模式的意图（笛卡尔目标）。
    /// 需要进行 IK 解算和路径规划。
    /// </summary>
    private RobotMotionCommand HandleTaskControl(RobotControlIntent intent)
    {
        // 1. 逆运动学 (IK) 解算：将笛卡尔目标转换为目标关节角度
        // 传入 _currentJointAngles 作为 IK 解算的起始点，有助于数值 IK 找到更合适的解。
        float[] targetJointAngles = kinematicsCalculator.SolveIK(
            intent.TargetPosition, 
            intent.TargetEulerAngles, 
            _currentJointAngles // 传入当前关节角度作为 IK 求解的初始猜测
        );

        if (targetJointAngles == null || targetJointAngles.Length != robotDOF)
        {
            Debug.LogError($"MotionPlanner: IK解算失败或结果关节数量不匹配 DOF ({robotDOF})。无法规划。", this);
            return new RobotMotionCommand();
        }

        // 2. 路径规划：根据选择的算法生成轨迹点序列
        List<float[]> trajectoryPoints = new List<float[]>();
        float defaultSpeedFactor = 1.0f; // 可以在这里定义控制模式的默认速度，或从UI传递

        switch (intent.SelectedPlanningAlgorithm)
        {
            case PlanningAlgorithm.JointSpaceFiveOrderPolynomial:
                Debug.Log("MotionPlanner: 执行关节空间五次多项式规划...");
                // pathPlanner.GenerateJointSpaceFiveOrderTrajectory 返回一个包含多个轨迹点的列表
                trajectoryPoints = pathPlanner.GenerateJointSpaceFiveOrderTrajectory(
                    _currentJointAngles, // 起始关节角度
                    targetJointAngles,   // 目标关节角度
                    trajectoryWaypointTimeStep, // 轨迹点时间间隔
                    defaultSpeedFactor, // 使用默认速度因子
                    totaltime
                );
                break;

            case PlanningAlgorithm.CartesianSpaceStraightLine:
                // Debug.Log("MotionPlanner: 执行笛卡尔空间直线规划...");
                // // pathPlanner.GenerateCartesianSpaceTrajectory 返回一个包含多个轨迹点的列表
                // // 注意：笛卡尔空间规划内部会多次调用 IK
                // trajectoryPoints = pathPlanner.GenerateCartesianSpaceTrajectory(
                //     GetEndEffectorCurrentPose(), // 起始末端位姿 (XYZ, Euler)
                //     intent.TargetPosition,       // 目标末端位置
                //     intent.TargetEulerAngles,    // 目标末端欧拉角
                //     trajectoryWaypointTimeStep,  // 轨迹点时间间隔
                //     kinematicsCalculator,        // 传入 KinematicsCalculator 用于每步转换
                //     _currentJointAngles,         // 将当前关节角度传递给笛卡尔规划器，供内部 IK 使用
                //     defaultSpeedFactor // 使用默认速度因子
                // );
                break;

            default:
                Debug.LogWarning($"MotionPlanner: 未知的规划算法: {intent.SelectedPlanningAlgorithm}。无法规划。", this);
                return new RobotMotionCommand();
        }

        if (trajectoryPoints == null || trajectoryPoints.Count == 0)
        {
            Debug.LogError("MotionPlanner: 轨迹规划未能生成任何轨迹点。", this);
            return new RobotMotionCommand();
        }

        // 更新内部记录的当前关节角度为轨迹的最后一个点（模拟已到达）
        _currentJointAngles = (float[])trajectoryPoints[trajectoryPoints.Count - 1].Clone();

        RobotMotionCommand command = RobotMotionCommand.CreateTrajectoryCommand(
            trajectoryPoints.ToArray(), // 将 List<float[]> 转换为 float[][]
            _commandCounter,
            defaultSpeedFactor // 使用默认速度因子
        );
        command.CommandType = "ExecuteTrajectory"; // 明确命令类型
        return command;
    }

    /// <summary>
    /// 获取当前末端执行器的位姿。
    /// 它将调用 KinematicsCalculator 的 SolveFK 方法。
    /// </summary>
    /// <returns>当前末端执行器的位置和欧拉角（作为 System.Tuple）。</returns>
    private Matrix4x4 GetEndEffectorCurrentPose()
    {
        // 调用 KinematicsCalculator 进行正运动学计算
        return kinematicsCalculator.SolveFK(_currentJointAngles); 
    }


    /// <summary>
    /// 处理演示模式的意图。
    /// 此时只需加载预设的轨迹。
    /// </summary>
    private RobotMotionCommand HandleDemonstration(RobotControlIntent intent)
    {
        // 演示模式：根据 DemoSequenceID 加载预设轨迹
        float[][] demoTrajectory = LoadDemoTrajectory(intent.DemoSequenceID);

        if (demoTrajectory == null || demoTrajectory.Length == 0)
        {
            Debug.LogError($"MotionPlanner: 无法加载演示序列 ID: {intent.DemoSequenceID}。", this);
            return new RobotMotionCommand();
        }

        // 更新内部记录的当前关节角度为轨迹的最后一个点
        _currentJointAngles = (float[])demoTrajectory[demoTrajectory.Length - 1].Clone();

        // 演示模式通常有预设的速度，但为了简单，这里也使用1.0f，
        // 实际可以从演示数据中读取。
        float demoSpeedFactor = 1.0f; 

        RobotMotionCommand command = RobotMotionCommand.CreateTrajectoryCommand(
            demoTrajectory,
            _commandCounter,
            demoSpeedFactor // 使用演示模式的速度因子
        );
        command.CommandType = "ExecuteDemo"; // 明确演示命令类型
        return command;
    }

    /// <summary>
    /// 模拟加载预设的演示轨迹。
    /// 实际中可以从 JSON、CSV 文件或 ScriptableObject 加载。
    /// </summary>
    /// <param name="demoID">演示序列的ID。</param>
    /// <returns>预设的关节角度轨迹。</returns>
    private float[][] LoadDemoTrajectory(int demoID)
    {
        // --- 示例演示轨迹数据 ---
        // 实际应用中，这会是一个更复杂的加载机制。
        // 这里只是为了演示，提供两个简单的硬编码轨迹。
        switch (demoID)
        {
            case 1: // 演示 1：简单的来回运动
                return new float[][]
                {
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 20, 30, -40, 10, 0 },
                    new float[] { -20, -30, 40, -10, 0 },
                    new float[] { 0, 0, 0, 0, 0 }
                };
            case 2: // 演示 2：更复杂的运动
                return new float[][]
                {
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 10, 20, 30, 40, 50 },
                    new float[] { 50, 40, 30, 20, 10 },
                    new float[] { 0, 0, 0, 0, 0 }
                };
            default:
                Debug.LogWarning($"MotionPlanner: 未找到演示序列 ID: {demoID}。", this);
                return null;
        }
    }

    /// <summary>
    /// 更新 MotionPlanner 内部记录的当前关节角度。
    /// 这个方法**应该由 RobotArmExecutor 或传感器在关节实际移动后调用**，
    /// 以保持规划器的“世界观”与实际机械臂同步。
    /// </summary>
    /// <param name="angles">机械臂当前的所有关节角度。</param>
    public void UpdateCurrentJointAngles(float[] angles)
    {
        if (angles != null && angles.Length == robotDOF)
        {
            _currentJointAngles = (float[])angles.Clone();
            // Debug.Log($"MotionPlanner: 当前关节角度已更新。J1:{_currentJointAngles[0]:F1}° ..."); // 调试时可以打开
        }
        else
        {
            Debug.LogWarning($"MotionPlanner: 尝试更新当前关节角度失败，数组为空或长度不匹配 DOF ({robotDOF})。", this);
        }
    }
}