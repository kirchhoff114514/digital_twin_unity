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
    private float _taskControlSegmentStartTime; // 记录当前任务控制指令开始的时间 (Time.time)
    [Tooltip("在任务控制模式下，机器人平滑过渡到目标位置的期望时间 (秒)。")]
    public float _taskControlMovementSmoothingDuration = 1.0f; // 默认1秒平滑过渡
    private float[] _iksolveResult; // 用于存储IK解算结果
    private float[] _iksolveResult_playmode; // 用于存储IK解算结果
    


    // 用于GripperControl模式

    // --- 当前时刻计算出的**期望目标**角度（提供给 SerialCommunicator 发送） ---
    // 这些是MotionPlanner在每一帧或每次CalculateDesiredOutput()调用时更新的值。
    private float[] _desiredJointAngles; // 当前期望的 5个关节角度

    // MotionPlanner需要这些信息作为IK的起点或轨迹规划的起始点。
    private float[] _currentJointAngles;
    private GripperState _currentGripperState=GripperState.Close; // 当前夹爪状态
    private GripperState _targetGripperState=GripperState.Close; // 夹爪状态
    private GripperState _intentGripperState=GripperState.Close; // 意图夹爪状态

    private Queue<RobotControlIntent> _taskQueue = new Queue<RobotControlIntent>();

    private RobotControlIntent _playmodeIntent; // 用于 PlayMode 的意图



    void Awake()
    {
        if (kinematicsCalculator == null || pathPlanner == null)
        {
            Debug.LogError("MotionPlanner: 依赖组件未设置。请在 Inspector 中拖拽赋值。", this);
            enabled = false;
            return;
        }

        // 初始化所有内部状态
        _currentJointAngles = new float[robotDOF];
        for (int i = 0; i < robotDOF; i++) _currentJointAngles[i] = 0f;

        _desiredJointAngles = (float[])_currentJointAngles.Clone();

        _jointTeachTargetAngles = (float[])_currentJointAngles.Clone();

        _currentActiveMode = ControlMode.JointSpaceTeaching; // 默认启动模式

        Debug.Log("MotionPlanner: 初始化完成。");
    }


    public void UpdateCurrentAngle(float[] currentJointAngles,GripperState currentGripperState)
    {
        if (currentJointAngles != null && currentJointAngles.Length == robotDOF)
        {
            _currentJointAngles = (float[])currentJointAngles.Clone();
            _currentGripperState = currentGripperState; // 更新夹爪状态
            // Debug.Log($"MotionPlanner: 实际状态更新。J1:{_currentActualJointAngles[0]:F1}°, 夹爪电机:{actualGripperAngle:F1}");
        }
        else
        {
            Debug.LogWarning($"MotionPlanner: 尝试更新当前实际关节角度失败，数组为空或长度不匹配 DOF ({robotDOF})。", this);
        }
    }

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
                _targetGripperState = intent.TargetGripperState; // 更新夹爪状态
                Debug.Log($"MotionPlanner: JointTeach模式意图已更新。J1: {_jointTeachTargetAngles[0]:F1}°, 夹爪状态: {intent.TargetGripperState}");
                break;

            case ControlMode.TaskControl:
                _taskControlTargetPosition = intent.TargetPosition;
                _taskControlTargetEulerAngles = intent.TargetEulerAngles;
                _targetGripperState = intent.TargetGripperState;
                Debug.Log($"MotionPlanner: TaskControl模式意图已更新111。目标位置: {_taskControlTargetPosition}, 目标姿态: {_taskControlTargetEulerAngles}, 夹爪状态: {intent.TargetGripperState}");
                _iksolveResult = kinematicsCalculator.SolveIK(
                    _taskControlTargetPosition,
                    _taskControlTargetEulerAngles,
                    _currentJointAngles // 以当前实际角度作为IK求解起点
                );
                // 当新的任务控制目标设定时，重置时间，以便从当前位置开始新的平滑过渡
                _taskControlSegmentStartTime = Time.time;
                Debug.Log($"MotionPlanner: TaskControl模式意图已更新。目标位置: {intent.TargetPosition}, 夹爪状态: {intent.TargetGripperState}");
                break;

            case ControlMode.PlayMode:
                _targetGripperState = intent.TargetGripperState;
                float[] temp;
                temp =kinematicsCalculator.SolveIK2(
                    intent.TargetPosition,
                    intent.TargetEulerAngles,
                    _currentJointAngles // 以当前实际角度作为IK求解起点
                );
                if (temp == null || temp.Length!= robotDOF){
                    _iksolveResult_playmode= _currentJointAngles;
                }else{
                    _iksolveResult_playmode = temp;
                }
                
                Debug.Log($"MotionPlanner: plymode模式意图已更新111。目标位置: {intent.TargetPosition}, 目标姿态: {intent.TargetEulerAngles}, 夹爪状态: {intent.TargetGripperState}");

                break;

            case ControlMode.GripperControl:
                _intentGripperState = intent.TargetGripperState;
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
    public Tuple<float[], GripperState> CalculateDesiredOutput(float deltaTime)
    {
        // 确保每次调用都基于当前实际角度，作为规划的起点
        float[] currentStartAngles = (float[])_currentJointAngles.Clone();

        switch (_currentActiveMode)
        {
            case ControlMode.JointSpaceTeaching:
                // JointTeach模式：直接将目标值作为期望输出
                _desiredJointAngles = (float[])_jointTeachTargetAngles.Clone();
                break;

            case ControlMode.TaskControl:


                if (_iksolveResult == null || _iksolveResult.Length != robotDOF)
                {
                    Debug.LogError($"MotionPlanner: TaskControl模式下IK解算失败或结果关节数量不匹配 DOF ({robotDOF})。保持当前位置。", this);
                    // IK失败时，保持当前实际关节角度作为目标，避免机器人跳动
                    _desiredJointAngles = (float[])currentStartAngles.Clone();
                    _targetGripperState = _currentGripperState; // 夹爪状态直接从任务控制状态获取
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
                            _iksolveResult[i],           // QF: IK解算出的目标关节角度
                            _taskControlMovementSmoothingDuration // T: 整个平滑过渡的时间
                        );
                        // 根据计算出的系数和已逝时间，计算当前时刻的期望关节角度
                        currentDesiredJoints[i] = coeffs[0] + coeffs[3] * t_clamped * t_clamped * t_clamped +
                                                  coeffs[4] * t_clamped * t_clamped * t_clamped * t_clamped +
                                                  coeffs[5] * t_clamped * t_clamped * t_clamped * t_clamped * t_clamped;
                    }
                    _desiredJointAngles = currentDesiredJoints;
                    _targetGripperState = _currentGripperState; // 夹爪状态直接从任务控制状态获取

                    // 如果已经到达平滑过渡的末尾（或超过），将期望关节角度精确设置为目标，避免浮点误差
                    if (elapsedTimeInSegment >= _taskControlMovementSmoothingDuration)
                    {
                        _desiredJointAngles = (float[])_iksolveResult.Clone();
                        // 夹爪角度已直接设置，无需再次更新
                    }
                }
                break;

            case ControlMode.PlayMode:
                    _desiredJointAngles =_iksolveResult_playmode.Clone() as float[]; // 使用 PlayMode 的 IK 解算结果
                    _targetGripperState = _currentGripperState; 
                    break;
               
            case ControlMode.GripperControl:
                // GripperControl模式：仅控制夹爪，关节保持当前状态
                _desiredJointAngles = (float[])currentStartAngles.Clone();
                _targetGripperState =_intentGripperState;
                break;

            default:
                // 未知模式或无活动模式时，保持当前实际角度
                _desiredJointAngles = (float[])currentStartAngles.Clone();
                _targetGripperState = _currentGripperState; // 夹爪状态直接从任务控制状态获取
                break;
        }

        return Tuple.Create(_desiredJointAngles,_targetGripperState );
    }




   
}