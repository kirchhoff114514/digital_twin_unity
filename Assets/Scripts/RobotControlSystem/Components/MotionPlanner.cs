// File: Assets/Scripts/RobotControlSystem/Components/MotionPlanner.cs
// Description: 机械臂的运动规划器，根据用户意图和当前实际状态，计算并提供目标角度。
//              此版本已修改以适应 PathPlanner 仅计算五次多项式系数的新架构。

using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq; // for .Clone()

/// <summary>
/// MotionPlanner 负责将高层级的用户控制意图（RobotControlIntent）
/// 转换为低层级的机械臂期望目标角度。它处理逆运动学、路径规划等，
/// 但不再直接生成完整轨迹，而是提供当前时刻的期望目标。
/// </summary>
public class MotionPlanner : MonoBehaviour
{
    // --- 公共引用（Inspector 或 RobotControlSystemManager 注入） ---
    [Header("依赖组件")]
    [Tooltip("拖拽 KinematicsCalculator 组件到此处。")]
    public KinematicsCalculator kinematicsCalculator;
    [Tooltip("拖拽 PathPlanner 组件到此处。")]
    public PathPlanner pathPlanner;

    [Header("规划参数")]
    [Tooltip("每个关节的自由度数量。例如，5轴机械臂就是5。")]
    public int robotDOF = 5;

    // --- 内部状态：用于计算目标角度的参数 ---
    private ControlMode _currentActiveMode; // 记录当前激活的控制模式

    // 用于JointTeach模式
    private float[] _jointTeachTargetAngles; // 用户直接设定的关节目标角度

    // 用于TaskControl模式
    private Vector3 _taskControlTargetPosition;     // 任务空间目标位置
    private Vector3 _taskControlTargetEulerAngles;  // 任务空间目标欧拉角
    private PlanningAlgorithm _taskControlPlanningAlgorithm; // 任务空间规划算法
    private GripperState _taskControlGripperState; // 夹爪目标状态 (Open/Close/None)
    private float _taskControlSegmentStartTime; // 记录当前任务控制指令开始的时间 (Time.time)
    [Tooltip("在任务控制模式下，机器人平滑过渡到目标位置的期望时间 (秒)。")]
    public float _taskControlMovementSmoothingDuration = 1.0f; // 默认1秒平滑过渡

    // 用于Demonstration模式
    private int _demoID; // 演示ID
    private float[][] _currentDemoTrajectory; // 当前加载的演示轨迹 (包含关节角度和夹爪电机角度)
    private float _demoExecutionTimer; // 演示计时器
    private float _demoWaypointTimeInterval = 0.05f; // 演示轨迹点间隔，需要与LoadDemoTrajectory中的时间概念匹配

    // 用于GripperControl模式
    private GripperState _gripperControlTargetState; // 夹爪控制模式下的目标状态

    // --- 当前时刻计算出的**期望目标**角度（提供给 SerialCommunicator 发送） ---
    // 这些是MotionPlanner在每一帧或每次CalculateDesiredOutput()调用时更新的值。
    private float[] _desiredJointAngles; // 当前期望的 5个关节角度
    private float _desiredGripperAngle; // 当前期望的 1个夹爪驱动电机角度 (只为Open/Close两个固定值)

    // --- 内部：机械臂当前实际关节角度和夹爪电机角度 (由外部 RobotArmExecutor/SerialCommunicator 提供) ---
    // MotionPlanner需要这些信息作为IK的起点或轨迹规划的起始点。
    private float[] _currentActualJointAngles;
    private float _currentActualGripperMotorAngle; // 存储实际夹爪电机角度

    void Awake()
    {
        if (kinematicsCalculator == null || pathPlanner == null)
        {
            Debug.LogError("MotionPlanner: 依赖组件未设置。请在 Inspector 中拖拽赋值。", this);
            enabled = false;
            return;
        }

        // 初始化所有内部状态
        _currentActualJointAngles = new float[robotDOF];
        for (int i = 0; i < robotDOF; i++) _currentActualJointAngles[i] = 0f;
        _currentActualGripperMotorAngle = ConvertGripperStateToMotorAngle(GripperState.Open); // 默认夹爪打开状态对应的电机角度

        _desiredJointAngles = (float[])_currentActualJointAngles.Clone();
        _desiredGripperAngle = _currentActualGripperMotorAngle;

        _jointTeachTargetAngles = (float[])_currentActualJointAngles.Clone();

        _currentActiveMode = ControlMode.JointSpaceTeaching; // 默认启动模式

        Debug.Log("MotionPlanner: 初始化完成。");
    }

    /// <summary>
    /// 更新 MotionPlanner 内部记录的**物理机械臂的实际状态**。
    /// 这个方法应该由 SerialCommunicator 在接收到下位机反馈后调用。
    /// </summary>
    /// <param name="actualJointAngles">物理机械臂当前的实际关节角度。</param>
    /// <param name="actualGripperAngle">物理夹爪驱动电机的实际角度。</param>
    public void UpdateCurrentActualState(float[] actualJointAngles)
    {
        if (actualJointAngles != null && actualJointAngles.Length == robotDOF)
        {
            _currentActualJointAngles = (float[])actualJointAngles.Clone();
            // Debug.Log($"MotionPlanner: 实际状态更新。J1:{_currentActualJointAngles[0]:F1}°, 夹爪电机:{actualGripperAngle:F1}");
        }
        else
        {
            Debug.LogWarning($"MotionPlanner: 尝试更新当前实际关节角度失败，数组为空或长度不匹配 DOF ({robotDOF})。", this);
        }
    }

    /// <summary>
    /// 接收 RobotInputManager 发布的机器人控制意图。
    /// 此方法仅更新 MotionPlanner 的内部目标参数，不立即计算或发送指令。
    /// </summary>
    /// <param name="intent">包含用户意图的 RobotControlIntent。</param>
    public void ProcessRobotControlIntent(RobotControlIntent intent)
    {
        Debug.Log($"MotionPlanner: 收到新的控制意图。模式: {intent.Mode}, ID: {intent.Timestamp}");

        _currentActiveMode = intent.Mode; // 更新当前激活的模式

        switch (intent.Mode)
        {
            case ControlMode.JointSpaceTeaching:
                if (intent.JointAngles == null || intent.JointAngles.Length != robotDOF)
                {
                    Debug.LogError($"MotionPlanner: JointTeach意图的关节数量不匹配DOF ({robotDOF}) 或为空。", this);
                    return;
                }
                _jointTeachTargetAngles = (float[])intent.JointAngles.Clone();
                _taskControlGripperState = intent.TargetGripperState; // 更新夹爪状态
                Debug.Log($"MotionPlanner: JointTeach模式意图已更新。J1: {_jointTeachTargetAngles[0]:F1}°, 夹爪状态: {intent.TargetGripperState}");
                break;

            case ControlMode.TaskControl:
                _taskControlTargetPosition = intent.TargetPosition;
                _taskControlTargetEulerAngles = intent.TargetEulerAngles;
                _taskControlPlanningAlgorithm = intent.SelectedPlanningAlgorithm; // 可用于未来选择不同算法
                _taskControlGripperState = intent.TargetGripperState;
                // 当新的任务控制目标设定时，重置时间，以便从当前位置开始新的平滑过渡
                _taskControlSegmentStartTime = Time.time;
                Debug.Log($"MotionPlanner: TaskControl模式意图已更新。目标位置: {intent.TargetPosition}, 夹爪状态: {intent.TargetGripperState}");
                break;

            case ControlMode.Demonstration:
                _demoID = intent.DemoSequenceID;
                _demoExecutionTimer = 0f; // 重置计时器
                _currentDemoTrajectory = LoadDemoTrajectory(_demoID); // 预加载演示轨迹
                if (_currentDemoTrajectory == null || _currentDemoTrajectory.Length == 0)
                {
                    Debug.LogError($"MotionPlanner: 无法加载演示序列 ID: {_demoID}。", this);
                    return;
                }
                Debug.Log($"MotionPlanner: Demo模式意图已更新。演示ID: {_demoID}, 轨迹点数: {_currentDemoTrajectory.Length}");
                break;

            case ControlMode.GripperControl:
                _gripperControlTargetState = intent.TargetGripperState;
                Debug.Log($"MotionPlanner: GripperControl模式意图已更新。夹爪状态: {intent.TargetGripperState}");
                break;

            default:
                Debug.LogWarning($"MotionPlanner: 未知的控制模式: {intent.Mode}。忽略此意图。", this);
                break;
        }
    }

    /// <summary>
    /// **核心方法：计算并返回当前帧期望发送给下位机的目标角度。**
    /// 这个方法应该由更高层的协调器（如 RobotControlSystemManager）每帧调用。
    /// </summary>
    /// <param name="deltaTime">自上次调用以来的时间间隔。</param>
    /// <returns>包含5个关节角度和1个夹爪电机角度的Tuple。</returns>
    public Tuple<float[], float> CalculateDesiredOutput(float deltaTime)
    {
        // 确保每次调用都基于当前实际角度，作为规划的起点
        float[] currentStartAngles = (float[])_currentActualJointAngles.Clone();

        switch (_currentActiveMode)
        {
            case ControlMode.JointSpaceTeaching:
                // JointTeach模式：直接将目标值作为期望输出
                _desiredJointAngles = (float[])_jointTeachTargetAngles.Clone();
                _desiredGripperAngle = ConvertGripperStateToMotorAngle(_taskControlGripperState); // 使用来自意图的夹爪状态
                break;

            case ControlMode.TaskControl:
                // TaskControl模式：进行IK解算并生成当前帧的平滑过渡点作为期望输出
                float[] ikResult = kinematicsCalculator.SolveIK(
                    _taskControlTargetPosition,
                    _taskControlTargetEulerAngles,
                    currentStartAngles // 以当前实际角度作为IK求解起点
                );

                if (ikResult == null || ikResult.Length != robotDOF)
                {
                    Debug.LogError($"MotionPlanner: TaskControl模式下IK解算失败或结果关节数量不匹配 DOF ({robotDOF})。保持当前位置。", this);
                    // IK失败时，保持当前实际关节角度作为目标，避免机器人跳动
                    _desiredJointAngles = (float[])currentStartAngles.Clone();
                    _desiredGripperAngle = ConvertGripperStateToMotorAngle(_taskControlGripperState); // 即使IK失败，也尝试更新夹爪
                }
                else
                {
                    // 计算当前运动段的已逝时间
                    float elapsedTimeInSegment = Time.time - _taskControlSegmentStartTime;
                    // 将已逝时间钳制在平滑过渡总时长内，确保不超过轨迹终点
                    float t_clamped = Mathf.Min(elapsedTimeInSegment, _taskControlMovementSmoothingDuration);

                    float[] currentDesiredJoints = new float[robotDOF];
                    for (int i = 0; i < robotDOF; i++)
                    {
                        // 计算每个关节的五次多项式系数 (从当前实际角度到IK目标角度)
                        float[] coeffs = pathPlanner.CalculateFiveOrderPolynomialCoefficients(
                            currentStartAngles[i], // Q0: 当前实际关节角度
                            ikResult[i],           // QF: IK解算出的目标关节角度
                            _taskControlMovementSmoothingDuration // T: 整个平滑过渡的时间
                        );
                        // 根据计算出的系数和已逝时间，计算当前时刻的期望关节角度
                        currentDesiredJoints[i] = coeffs[0] + coeffs[3] * t_clamped * t_clamped * t_clamped +
                                                  coeffs[4] * t_clamped * t_clamped * t_clamped * t_clamped +
                                                  coeffs[5] * t_clamped * t_clamped * t_clamped * t_clamped * t_clamped;
                    }
                    _desiredJointAngles = currentDesiredJoints;

                    // 夹爪直接设置为目标状态对应的电机角度，不再进行平滑过渡
                    _desiredGripperAngle = ConvertGripperStateToMotorAngle(_taskControlGripperState);

                    // 如果已经到达平滑过渡的末尾（或超过），将期望关节角度精确设置为目标，避免浮点误差
                    if (elapsedTimeInSegment >= _taskControlMovementSmoothingDuration)
                    {
                        _desiredJointAngles = (float[])ikResult.Clone();
                        // 夹爪角度已直接设置，无需再次更新
                    }
                }
                break;

            case ControlMode.Demonstration:
                // 演示模式：根据预加载的轨迹按时间步进
                _demoExecutionTimer += deltaTime;

                if (_currentDemoTrajectory == null || _currentDemoTrajectory.Length == 0)
                {
                    Debug.LogWarning("MotionPlanner: 演示轨迹为空或未加载。", this);
                    _desiredJointAngles = (float[])currentStartAngles.Clone();
                    _desiredGripperAngle = ConvertGripperStateToMotorAngle(GripperState.Open);
                    break;
                }

                // 计算当前应到达的轨迹点索引
                int targetWaypointIndex = Mathf.Min(
                    (int)(_demoExecutionTimer / _demoWaypointTimeInterval),
                    _currentDemoTrajectory.Length - 1
                );

                if (targetWaypointIndex >= _currentDemoTrajectory.Length)
                {
                    // 演示结束，停留在最后一个点
                    _desiredJointAngles = new float[robotDOF];
                    Array.Copy(_currentDemoTrajectory[_currentDemoTrajectory.Length - 1], 0, _desiredJointAngles, 0, robotDOF);
                    _desiredGripperAngle = _currentDemoTrajectory[_currentDemoTrajectory.Length - 1][robotDOF]; // 从轨迹中获取夹爪电机角度
                    Debug.Log("MotionPlanner: 演示播放完毕。");
                }
                else
                {
                    _desiredJointAngles = new float[robotDOF];
                    Array.Copy(_currentDemoTrajectory[targetWaypointIndex], 0, _desiredJointAngles, 0, robotDOF);
                    _desiredGripperAngle = _currentDemoTrajectory[targetWaypointIndex][robotDOF]; // 从轨迹中获取夹爪电机角度
                }
                break;

            case ControlMode.GripperControl:
                // GripperControl模式：仅控制夹爪，关节保持当前状态
                _desiredJointAngles = (float[])currentStartAngles.Clone();
                _desiredGripperAngle = ConvertGripperStateToMotorAngle(_gripperControlTargetState);
                break;

            default:
                // 未知模式或无活动模式时，保持当前实际角度
                _desiredJointAngles = (float[])currentStartAngles.Clone();
                _desiredGripperAngle =  ConvertGripperStateToMotorAngle(GripperState.Open);
                break;
        }

        return Tuple.Create(_desiredJointAngles, _desiredGripperAngle);
    }


    /// <summary>
    /// 模拟加载预设的演示轨迹。
    /// **重要：请确保这里返回的 float[] 数组的维度与 robotDOF + 1 (for gripper) 匹配。**
    /// 数组的最后一个元素应为夹爪电机角度。
    /// </summary>
    /// <param name="demoID">演示序列的ID。</param>
    /// <returns>预设的关节角度和夹爪电机角度轨迹。</returns>
    private float[][] LoadDemoTrajectory(int demoID)
    {
        // --- 示例演示轨迹数据 ---
        // 实际应用中，这会是一个更复杂的加载机制，例如从文件加载。
        // 这里的轨迹点间隔（_demoWaypointTimeInterval）需要与实际演示数据的生成间隔匹配。
        // float[] 的长度应该是 robotDOF + 1 (5关节 + 1夹爪电机角度)。
        switch (demoID)
        {
            case 1: // 演示 1：简单的来回运动 (5关节 + 夹爪)
                _demoWaypointTimeInterval = 0.1f; // 假设这个演示的轨迹点是每0.1秒一个
                return new float[][]
                {
                    new float[] { 0, 0, 0, 0, 0, ConvertGripperStateToMotorAngle(GripperState.Open) }, // Open
                    new float[] { 20, 30, -40, 10, 0, ConvertGripperStateToMotorAngle(GripperState.Open) },
                    new float[] { -20, -30, 40, -10, 0, ConvertGripperStateToMotorAngle(GripperState.Close) }, // Close
                    new float[] { 0, 0, 0, 0, 0, ConvertGripperStateToMotorAngle(GripperState.Open) } // Open
                };
            case 2: // 演示 2：更复杂的运动 (5关节 + 夹爪)
                _demoWaypointTimeInterval = 0.05f; // 假设这个演示的轨迹点是每0.05秒一个
                return new float[][]
                {
                    new float[] { 0, 0, 0, 0, 0, ConvertGripperStateToMotorAngle(GripperState.Close) }, // Close
                    new float[] { 10, 20, 30, 40, 50, ConvertGripperStateToMotorAngle(GripperState.Close) },
                    new float[] { 50, 40, 30, 20, 10, ConvertGripperStateToMotorAngle(GripperState.Open) }, // Open
                    new float[] { 0, 0, 0, 0, 0, ConvertGripperStateToMotorAngle(GripperState.Close) } // Close
                };
            default:
                Debug.LogWarning($"MotionPlanner: 未找到演示序列 ID: {demoID}。", this);
                return null;
        }
    }

    /// <summary>
    /// 将 GripperState 转换为夹爪驱动电机角度。
    /// 假设 Open 对应 90度，Close 对应 0度。
    /// </summary>
    private float ConvertGripperStateToMotorAngle(GripperState state)
    {
        switch (state)
        {
            case GripperState.Open:
                return 90f; // 示例：打开对应的电机角度
            case GripperState.Close:
                return 0f; // 示例：关闭对应的电机角度
            case GripperState.None:
            default:
                // 如果是None，表示没有明确的夹爪指令，返回当前夹爪电机角度
                return _currentActualGripperMotorAngle;
        }
    }
}