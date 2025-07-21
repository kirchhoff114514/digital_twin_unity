// File: Assets/Scripts/RobotControlSystem/Core/RobotActualState.cs
// Description: 定义了物理机械臂当前反馈的实际关节角度和夹爪的开闭状态。

using UnityEngine; // For Mathf.Clamp01 (though no longer directly used for openness range)
using System; // For DateTime



/// <summary>
/// 封装物理机械臂当前反馈的实际关节角度和夹爪的开闭状态。
/// 这个数据结构由 SerialCommunicator 接收并传递给 RobotArmExecutor/MotionPlanner。
/// </summary>
[System.Serializable]
public struct RobotActualState
{
    public float[] JointAngles;     // 物理机械臂的实际关节角度 (5个)
    public GripperState GripperState; // 物理夹爪的实际开闭状态 (1个)

    // 可以添加时间戳，用于同步或调试
    public long Timestamp;

    /// <summary>
    /// 静态工厂方法：创建一个新的 RobotActualState 实例。
    /// </summary>
    /// <param name="jointAngles">实际关节角度数组。</param>
    /// <param name="gripperState">实际夹爪的开闭状态。</param>
    /// <returns>新的 RobotActualState 实例。</returns>
    public static RobotActualState Create(float[] jointAngles, GripperState gripperState)
    {
        RobotActualState state = new RobotActualState();
        state.JointAngles = (float[])jointAngles.Clone(); // 克隆数组以防止外部修改
        state.GripperState = gripperState;
        state.Timestamp = System.DateTime.Now.Ticks; // 添加时间戳
        return state;
    }
}