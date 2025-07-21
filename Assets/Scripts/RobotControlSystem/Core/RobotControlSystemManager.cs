// File: Assets/Scripts/RobotControlSystem/Core/RobotControlSystemManager.cs
// Description: 机器人控制系统的中央管理器，负责初始化、连接和协调所有核心组件。
//              此版本已更新，以适应 RobotArmExecutor 仅处理接收数据，
//              并由 SerialCommunicator 负责发送指令的新逻辑，并适配 GripperState 枚举。

using UnityEngine;
using System; // For Tuple

/// <summary>
/// RobotControlSystemManager 是整个机器人控制系统的主要入口点。
/// 它负责链接 InputManager, MotionPlanner, SerialCommunicator 和 RobotArmExecutor。
/// </summary>
public class RobotControlSystemManager : MonoBehaviour
{
    [Header("核心系统组件引用")]
    [Tooltip("拖拽 RobotInputManager 组件到此处。")]
    public RobotInputManager robotInputManager;

    [Tooltip("拖拽 MotionPlanner 组件到此处。")]
    public MotionPlanner motionPlanner;

    [Tooltip("拖拽 RobotArmExecutor 组件到此处。")]
    public RobotArmExecutor robotArmExecutor;

    [Tooltip("拖拽 SerialCommunicator 组件到此处。")]
    public SerialCommunicator serialCommunicator; // 新增对 SerialCommunicator 的引用

    void Awake()
    {
        Debug.Log("RobotControlSystemManager: 开始初始化系统...");

        // 1. 检查所有必需组件的引用是否已设置
        if (!ValidateReferences())
        {
            Debug.LogError("RobotControlSystemManager: 关键组件引用缺失，系统无法启动。", this);
            enabled = false; // 禁用此组件，防止后续错误
            return;
        }

        // 2. 建立事件订阅链
        // 当 RobotInputManager 发布新的控制意图时，MotionPlanner 将处理它。
        robotInputManager.OnRobotControlIntentUpdated += motionPlanner.ProcessRobotControlIntent;
        Debug.Log("RobotControlSystemManager: RobotInputManager.OnRobotControlIntentUpdated 已订阅到 MotionPlanner.ProcessRobotControlIntent。");

        // 当 SerialCommunicator 从下位机接收到实际数据时，RobotArmExecutor 将执行（处理）它。
        // SerialCommunicator.OnActualRobotStateReceived 事件现在传递 GripperState 枚举。
        // RobotArmExecutor.UpdateDigitalTwin 方法已更新以接受 GripperState。
        serialCommunicator.OnActualRobotStateReceived += robotArmExecutor.UpdateDigitalTwin;
        Debug.Log("RobotControlSystemManager: SerialCommunicator.OnActualRobotStateReceived 已订阅到 RobotArmExecutor.UpdateDigitalTwin。");

        // RobotArmExecutor 在处理接收到的数据后，触发实际状态更新事件，供 MotionPlanner 订阅。
        // RobotArmExecutor.OnActualStateUpdated 事件现在传递 GripperState 枚举。
        // 确保 MotionPlanner.UpdateCurrentActualState 方法也已更新以接受 GripperState。
        robotArmExecutor.OnActualStateUpdated += motionPlanner.UpdateCurrentActualState;
        Debug.Log("RobotControlSystemManager: RobotArmExecutor.OnActualStateUpdated 已订阅到 MotionPlanner.UpdateCurrentActualState。");

        Debug.Log("RobotControlSystemManager: 系统初始化完成，事件链已建立。");
    }

    void OnDestroy()
    {
        // 在销毁时取消订阅事件，防止内存泄漏或空引用错误
        if (robotInputManager != null)
        {
            robotInputManager.OnRobotControlIntentUpdated -= motionPlanner.ProcessRobotControlIntent;
            Debug.Log("RobotControlSystemManager: 已取消订阅 RobotInputManager.OnRobotControlIntentUpdated。");
        }

        if (serialCommunicator != null)
        {
            serialCommunicator.OnActualRobotStateReceived -= robotArmExecutor.UpdateDigitalTwin;
            Debug.Log("RobotControlSystemManager: 已取消订阅 SerialCommunicator.OnActualRobotStateReceived。");
        }

        if (robotArmExecutor != null)
        {
            robotArmExecutor.OnActualStateUpdated -= motionPlanner.UpdateCurrentActualState;
            Debug.Log("RobotControlSystemManager: 已取消订阅 RobotArmExecutor.OnActualStateUpdated。");
        }

        Debug.Log("RobotControlSystemManager: 系统清理完成。");
    }

    /// <summary>
    /// Unity 的 Update 方法，每帧调用一次。
    /// 这是机器人控制系统的主循环，负责获取期望目标并将其发送给通信器。
    /// </summary>
    void Update()
    {
        // 每帧调用 MotionPlanner 来计算当前期望发送给机械臂的关节角度和夹爪角度。
        // MotionPlanner.CalculateDesiredOutput 方法会返回一个 Tuple<float[], float>
        // 其中 Item1 是关节角度数组，Item2 是夹爪电机角度（即使夹爪内部以 GripperState 管理，
        // 对硬件发送时通常仍需要一个浮点数）。
        Tuple<float[], float> desiredOutput = motionPlanner.CalculateDesiredOutput(Time.deltaTime);

        // 将计算出的期望目标直接传递给 SerialCommunicator 进行发送。
        if (desiredOutput != null && serialCommunicator != null)
        {
            serialCommunicator.SendDesiredAnglesToHardware(desiredOutput.Item1, desiredOutput.Item2);
        }
    }

    /// <summary>
    /// 验证所有必需的组件引用是否已在 Inspector 中设置。
    /// </summary>
    /// <returns>如果所有引用都有效则为 true，否则为 false。</returns>
    private bool ValidateReferences()
    {
        bool allGood = true;

        if (robotInputManager == null)
        {
            Debug.LogError("RobotControlSystemManager: 'Robot Input Manager' 引用未设置。请在 Inspector 中拖拽赋值。", this);
            allGood = false;
        }
        if (motionPlanner == null)
        {
            Debug.LogError("RobotControlSystemManager: 'Motion Planner' 引用未设置。请在 Inspector 中拖拽赋值。", this);
            allGood = false;
        }
        if (robotArmExecutor == null)
        {
            Debug.LogError("RobotControlSystemManager: 'Robot Arm Executor' 引用未设置。请在 Inspector 中拖拽赋值。", this);
            allGood = false;
        }
        if (serialCommunicator == null)
        {
            Debug.LogError("RobotControlSystemManager: 'Serial Communicator' 引用未设置。请在 Inspector 中拖拽赋值。", this);
            allGood = false;
        }

        return allGood;
    }
}