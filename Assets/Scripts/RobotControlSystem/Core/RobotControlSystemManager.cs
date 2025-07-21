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


        Debug.Log("RobotControlSystemManager: 系统清理完成。");
    }


    void Update()
    {
        
        Tuple<float[], GripperState> desiredOutput = motionPlanner.CalculateDesiredOutput(Time.deltaTime);
        if (desiredOutput!= null)
        {
            // 将期望的关节角度和夹爪状态发送给 RobotArmExecutor
            robotArmExecutor.SetJointAngles(desiredOutput.Item1, desiredOutput.Item2);
            Debug.Log($"RobotControlSystemManager: 发送期望关节角度和夹爪状态到 RobotArmExecutor。");
        }
        else
        {
            Debug.LogWarning("RobotControlSystemManager: MotionPlanner 返回的期望输出为 null，可能是当前没有有效意图或计算结果。");
        }
        motionPlanner.UpdateCurrentAngle(robotArmExecutor._currentJointAngles, robotArmExecutor._currentGripperState);
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


        return allGood;
    }
}