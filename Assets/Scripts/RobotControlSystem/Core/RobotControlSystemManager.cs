// File: Assets/Scripts/RobotControlSystem/Core/RobotControlSystemManager.cs
// Description: 机器人控制系统的中央管理器，负责初始化、连接和协调所有核心组件。
//              此版本已更新，以适应 RobotArmExecutor 仅处理接收数据，
//              并由 SerialCommunicator 负责发送指令的新逻辑，并适配 GripperState 枚举。

using UnityEngine;
using System;

/// <summary>
/// RobotControlSystemManager 是整个机器人控制系统的主要入口点。
/// 它负责链接 InputManager, MotionPlanner, SerialCommunicator 和 RobotArmExecutor。
/// </summary>
/// 


public enum MotorType
{//动力类型，默认为舵机
    Servo=0,Stepper=1
}

public class RobotControlSystemManager : MonoBehaviour
{
    [Header("核心系统组件引用")]
    [Tooltip("拖拽 RobotInputManager 组件到此处。")]
    public RobotInputManager robotInputManager;

    [Tooltip("拖拽 MotionPlanner 组件到此处。")]
    public MotionPlanner motionPlanner;

    [Tooltip("拖拽 RobotArmExecutor 组件到此处。")]
    public RobotArmExecutor robotArmExecutor;


    private int time_flag = -1; 

    Tuple<float[], GripperState> last_desiredOutput = null; // 用于存储上一次的期望输出;
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

        string[] ports=SerialManager.Instance.ScanPort();
        if (ports != null){
            foreach (string port in ports){
                SerialManager.Instance.ConnectSerialPort(port, 115200); // 连接串口
                Debug.Log("RobotControlSystemManager: 已连接串口：" + port);
            }
        }

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

    public MotorType motorType = MotorType.Servo; // motorType 设为 0（Servo）
         
    void Update()
    {
        
        Tuple<float[], GripperState> desiredOutput = motionPlanner.CalculateDesiredOutput(Time.deltaTime);
        float[] jointAngles = desiredOutput?.Item1;

        if (desiredOutput != null && jointAngles != null && desiredOutput != last_desiredOutput )
        {

            robotArmExecutor.SetJointAngles(desiredOutput.Item1, desiredOutput.Item2);
            Debug.Log($"RobotControlSystemManager: 发送期望关节角度和夹爪状态到 RobotArmExecutor。");

            
            // 遍历关节角度数组，依次发送每个关节的角度
            for (int i = 0; i < 4; i++)
            {
                int motorID = i + 1; 
                int angle = (int)jointAngles[i];

                // 发送串口数据
                SerialManager.Instance.SendData((int)motorType, motorID, angle);
                Debug.Log($"RobotControlSystemManager: 已通过串口发送 ID {motorID} 的关节角度: {angle}");
            }       
            SerialManager.Instance.SendData(1, 0, (int)jointAngles[4]);
            Debug.Log($"RobotControlSystemManager: 已通过串口发送 ID {0} 的关节角度: {(int)jointAngles[4]}");
            SerialManager.Instance.SendData(0, 5, (int)desiredOutput.Item2); //发送夹爪状态
            // time_flag++;
            // switch (time_flag) {
            //     case 0:
            //         SerialManager.Instance.SendData(0, 1, (int)jointAngles[0]); //发送夹爪状态
            //         break;
            //     case 2:
            //         SerialManager.Instance.SendData(0, 2, (int)jointAngles[1]); //发送夹爪状态
            //         break;
            //     case 4:
            //         SerialManager.Instance.SendData(0, 3, (int)jointAngles[2]); //发送夹爪状态
            //         break;
            //     case 6:
            //         SerialManager.Instance.SendData(0, 4, (int)jointAngles[3]); //发送夹爪状态
            //         break;
            //     case 8:
            //         SerialManager.Instance.SendData(1, 0, (int)jointAngles[4]); //发送夹爪状态
            //         break;
            //     case 10:
            //         SerialManager.Instance.SendData(0, 5, (int)desiredOutput.Item2); //发送夹爪状态
            //         time_flag = -1;
            //         break;
            //     default:
            //         break;
            // }
            last_desiredOutput = desiredOutput;
        }
        else
        {
            Debug.LogWarning("RobotControlSystemManager: MotionPlanner 返回的期望输出为 null 或关节角度数组长度不足 6，无法发送数据。");
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