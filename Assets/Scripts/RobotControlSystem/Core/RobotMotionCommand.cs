// File: Assets/Scripts/RobotControlSystem/Core/RobotMotionCommand.cs
// Description: 定义机器人运动指令数据结构，包含轨迹和执行参数。

using UnityEngine;

/// <summary>
/// 封装机器人机械臂的运动指令。
/// 包含要执行的关节角度轨迹和相关的执行参数。
/// </summary>
[System.Serializable] // 使其在Unity Inspector中可见，便于调试
public struct RobotMotionCommand
{
    /// <summary>
    /// 命令的唯一标识符。
    /// </summary>
    public int CommandID;

    /// <summary>
    /// 命令的类型，例如 "MoveToPoint", "ExecuteTrajectory", "ExecuteDemo"。
    /// </summary>
    public string CommandType;

    /// <summary>
    /// 包含机器人每个关节在时间序列上的目标角度。
    /// 外层数组代表时间步，内层数组代表该时间步上每个关节的角度。
    /// float[时间点索引][关节索引]
    /// </summary>
    public float[][] TrajectoryJointAngles;

    /// <summary>
    /// 命令的执行速度因子。1.0f为正常速度，0.5f为半速，2.0f为两倍速。
    /// 这个速度因子会影响 MotionPlanner 生成轨迹的实际执行时间，
    /// 也会被 ArmExecutor 用来调整旋转速度。
    /// </summary>
    public float ExecutionSpeed; 

    /// <summary>
    /// 静态工厂方法：创建一个包含单个目标点的运动命令（通常用于示教模式）。
    /// </summary>
    /// <param name="targetAngles">所有关节的目标角度。</param>
    /// <param name="commandID">命令ID。</param>
    /// <param name="speed">执行速度因子。</param>
    /// <returns>新的 RobotMotionCommand 实例。</returns>
    public static RobotMotionCommand CreateSinglePointCommand(float[] targetAngles, int commandID, float speed)
    {
        return new RobotMotionCommand
        {
            CommandID = commandID,
            CommandType = "MoveToPoint",
            TrajectoryJointAngles = new float[][] { targetAngles },
            ExecutionSpeed = speed
        };
    }

    /// <summary>
    /// 静态工厂方法：创建一个包含完整轨迹序列的运动命令。
    /// </summary>
    /// <param name="trajectory">包含所有轨迹点的关节角度序列。</param>
    /// <param name="commandID">命令ID。</param>
    /// <param name="speed">执行速度因子。</param>
    /// <returns>新的 RobotMotionCommand 实例。</returns>
    public static RobotMotionCommand CreateTrajectoryCommand(float[][] trajectory, int commandID, float speed)
    {
        return new RobotMotionCommand
        {
            CommandID = commandID,
            CommandType = "ExecuteTrajectory",
            TrajectoryJointAngles = trajectory,
            ExecutionSpeed = speed
        };
    }
}