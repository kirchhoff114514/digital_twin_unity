using UnityEngine; // 用于 Vector3
using System; // 用于 DateTime 和 TimeSpan

/// <summary>
/// 定义机械臂的控制模式。
/// </summary>
public enum ControlMode
{
    JointSpaceTeaching, // 关节空间示教（直接控制关节角度）
    TaskControl,        // 任务空间控制（控制末端位置和姿态）
    // Demonstration,      // 演示模式（播放预设轨迹） - 已移除，或根据需要注释掉
    GripperControl,     // 新增：专门用于夹爪控制的模式
    PlayMode            // 新增：游戏/操作模式（键盘控制，有放置/回家功能）
}



public enum GripperState
{
    Open,    // 打开夹爪
    Close,   // 关闭夹爪
    None     // 无动作
}

/// <summary>
/// RobotControlIntent 封装了用户或高层系统对机械臂的意图。
/// 这是一个只读的数据结构，通过静态工厂方法创建。
/// </summary>
public struct RobotControlIntent
{
    public ControlMode Mode { get; private set; }
    public long Timestamp { get; private set; } // 意图创建时间戳，用于唯一标识

    // --- 关节空间示教模式相关 (也用于PlayMode下的键盘关节控制) ---
    public float[] JointAngles { get; private set; } // 在示教模式下，直接的目标关节角度

    // --- 任务空间控制模式相关 (也用于PlayMode下的放置/回家功能) ---
    public Vector3 TargetPosition { get; private set; }    // 目标末端位置 (XYZ)
    public Vector3 TargetEulerAngles { get; private set; } // 目标末端姿态 (Pitch, Roll, Yaw)

    // --- 统一的末端夹爪控制相关 ---
    public GripperState TargetGripperState { get; private set; } // 夹爪目标状态 (Open/Close/None)

    // // --- 演示模式相关 (已移除或根据需求决定是否保留) ---
    // public int DemoSequenceID { get; private set; } // 演示序列的ID


    // 私有构造函数，强制通过工厂方法创建
    private RobotControlIntent(ControlMode mode)
    {
        Mode = mode;
        Timestamp = DateTime.Now.Ticks; // 使用当前时间戳作为唯一ID

        // 初始化所有字段以避免警告
        JointAngles = null;
        TargetPosition = Vector3.zero;
        TargetEulerAngles = Vector3.zero;
        TargetGripperState = GripperState.None; // 默认无夹爪控制意图
    }

    /// <summary>
    /// 工厂方法：创建关节空间示教意图 (也用于PlayMode下的键盘关节控制)。
    /// </summary>
    /// <param name="jointAngles">目标关节角度数组。</param>
    /// <returns>关节空间示教意图。</returns>
    public static RobotControlIntent CreateJointSpaceTeachingIntent(float[] jointAngles)
    {
        RobotControlIntent intent = new RobotControlIntent(ControlMode.JointSpaceTeaching);
        intent.JointAngles = (float[])jointAngles.Clone(); // 克隆数组以防止外部修改
        return intent;
    }

    /// <summary>
    /// 工厂方法：创建任务空间控制意图 (也用于PlayMode下的放置/回家功能)。
    /// </summary>
    /// <param name="targetPosition">末端目标位置。</param>
    /// <param name="targetEulerAngles">末端目标欧拉角 (Pitch, Roll, Yaw)。</param>
    /// <param name="algorithm">选定的规划算法。</param>
    /// <returns>任务空间控制意图。</returns>
    public static RobotControlIntent CreateTaskControlIntent(Vector3 targetPosition, Vector3 targetEulerAngles)
    {
        RobotControlIntent intent = new RobotControlIntent(ControlMode.TaskControl);
        intent.TargetPosition = targetPosition;
        intent.TargetEulerAngles = targetEulerAngles;
        return intent;
    }



    /// <summary>
    /// 工厂方法：创建独立的夹爪控制意图。
    /// </summary>
    /// <param name="gripperState">夹爪目标状态 (Open/Close)。</param>
    /// <returns>夹爪控制意图。</returns>
    public static RobotControlIntent CreateGripperControlIntent(GripperState gripperState)
    {
        if (gripperState == GripperState.None)
        {
            Debug.LogWarning("Creating a GripperControlIntent with GripperState.None is usually not intended. Please specify Open or Close.");
        }
        RobotControlIntent intent = new RobotControlIntent(ControlMode.GripperControl);
        intent.TargetGripperState = gripperState;
        return intent;
    }

    /// <summary>
    /// 工厂方法：创建Play模式意图。
    /// 此意图通常用于激活Play模式的UI/逻辑状态，实际的关节控制或任务控制会通过其他意图发出。
    /// </summary>
    /// <returns>Play模式意图。</returns>
    public static RobotControlIntent CreatePlayModeIntent()
    {
        return new RobotControlIntent(ControlMode.PlayMode);
    }
}