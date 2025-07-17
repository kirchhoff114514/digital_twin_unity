// File: Assets/Scripts/RobotControlSystem/RobotSpecific/PathPlanner.cs
// Description: 机械臂的路径规划器，用于生成关节空间或笛卡尔空间的平滑轨迹。

using UnityEngine;
using System.Collections.Generic;
using System; // For Tuple
using System.Linq; // For debugging join

public class PathPlanner : MonoBehaviour
{
    [Tooltip("机械臂的自由度数量。应与 KinematicsCalculator 中的 DOF 匹配。")]
    public int robotDOF = 5;

    void Awake()
    {
        // 确保 DOF 与 KinematicsCalculator 匹配（最佳实践是在 KinematicsCalculator 中设置，并在此处引用）
        // 或者，在 Manager 中统一管理 DOF
    }

    /// <summary>
    /// 关节空间五次多项式轨迹规划器占位符。
    /// 实际中会生成从起点到终点，速度和加速度平滑的关节角度序列。
    /// </summary>
    /// <param name="startJointAngles">起始关节角度。</param>
    /// <param name="targetJointAngles">目标关节角度。</param>
    /// <param name="timeStep">每个轨迹点之间的时间间隔（秒）。</param>
    /// <param name="speedFactor">轨迹执行的速度因子。</param>
    /// <returns>包含关节角度数组的轨迹点列表。</returns>
    public List<float[]> GenerateJointSpaceFiveOrderTrajectory(float[] startJointAngles, float[] targetJointAngles, float timeStep, float speedFactor)
    {
        if (startJointAngles == null || targetJointAngles == null || startJointAngles.Length != robotDOF || targetJointAngles.Length != robotDOF)
        {
            Debug.LogError($"Path Planner: 关节空间规划输入关节数量不匹配 DOF ({robotDOF}) 或为空。", this);
            return new List<float[]>();
        }

        Debug.Log($"Path Planner (Placeholder): 生成关节空间五次多项式轨迹。从 [{string.Join(", ", startJointAngles.Select(a => a.ToString("F1")))}] 到 [{string.Join(", ", targetJointAngles.Select(a => a.ToString("F1")))}]");
        List<float[]> trajectory = new List<float[]>();

        // --- 这是一个占位符实现 ---
        // 实际的五次多项式规划需要计算多项式系数，然后按时间步长采样。
        // 这里我们只生成几个线性插值的点来模拟一个简单的轨迹。
        // numPoints 应该考虑规划的总时长和 timeStep
        float totalDuration = 1.0f / Mathf.Max(0.1f, speedFactor); // 假设默认规划时长为1秒，速度因子越高，实际时间越短
        int numPoints = Mathf.Max(2, Mathf.CeilToInt(totalDuration / timeStep));
        
        for (int i = 0; i <= numPoints; i++)
        {
            float t = (float)i / numPoints; // 插值因子，从0到1
            float[] interpolatedAngles = new float[robotDOF];
            for (int j = 0; j < robotDOF; j++)
            {
                // 线性插值模拟平滑
                interpolatedAngles[j] = Mathf.Lerp(startJointAngles[j], targetJointAngles[j], t);
            }
            trajectory.Add(interpolatedAngles);
        }

        Debug.Log($"Path Planner (Placeholder): 生成了 {trajectory.Count} 个关节空间轨迹点。");
        return trajectory;
    }

    /// <summary>
    /// 笛卡尔空间直线路径规划器占位符。
    /// 实际中会在笛卡尔空间生成一条直线，然后每一步调用 IK 转换为关节角度。
    /// </summary>
    /// <param name="startPose">起始末端位姿 (位置, 欧拉角)。</param>
    /// <param name="targetPosition">目标末端位置。</param>
    /// <param name="targetEulerAngles">目标末端欧拉角。</param>
    /// <param name="timeStep">每个轨迹点之间的时间间隔（秒）。</param>
    /// <param name="kinematicsCalculator">运动学计算器实例（用于IK）。</param>
    /// <param name="currentJointAngles">当前关节角度，作为每次IK解算的起始猜测值。</param> 
    /// <param name="speedFactor">轨迹执行的速度因子。</param>
    /// <returns>包含关节角度数组的轨迹点列表。</returns>
    public List<float[]> GenerateCartesianSpaceTrajectory(Tuple<Vector3, Vector3> startPose, Vector3 targetPosition, Vector3 targetEulerAngles, float timeStep, KinematicsCalculator kinematicsCalculator, float[] currentJointAngles, float speedFactor)
    {
        if (kinematicsCalculator == null)
        {
            Debug.LogError("Path Planner: KinematicsCalculator 引用为空，无法进行笛卡尔空间规划。", this);
            return new List<float[]>();
        }
        if (currentJointAngles == null || currentJointAngles.Length != robotDOF)
        {
            Debug.LogError($"Path Planner: 传入的当前关节角度数组为空或长度不匹配 DOF ({robotDOF})。请确保提供有效的初始猜测。", this);
            return new List<float[]>();
        }

        Debug.Log($"Path Planner (Placeholder): 生成笛卡尔空间直线轨迹。从 Pos:{startPose.Item1}, Euler:{startPose.Item2} 到 Pos:{targetPosition}, Euler:{targetEulerAngles}");
        List<float[]> trajectory = new List<float[]>();

        // --- 这是一个占位符实现 ---
        // 实际的笛卡尔空间规划需要计算路径上的中间点，然后对每个中间点调用 IK。
        float totalDuration = 1.0f / Mathf.Max(0.1f, speedFactor); // 确保除数不为0或过小
        int numCartesianPoints = Mathf.Max(2, Mathf.CeilToInt(totalDuration / timeStep));

        // 维护一个当前IK猜测值，每次迭代更新，确保IK从上一步的结果开始，提高收敛性。
        // 初始猜测值使用传入的 currentJointAngles
        float[] ikGuessJointAngles = (float[])currentJointAngles.Clone(); 

        for (int i = 0; i <= numCartesianPoints; i++)
        {
            float t = (float)i / numCartesianPoints;
            // 线性插值笛卡尔位置和姿态
            Vector3 currentPos = Vector3.Lerp(startPose.Item1, targetPosition, t);
            Vector3 currentEuler = Vector3.Lerp(startPose.Item2, targetEulerAngles, t);

            // 对每个中间位姿调用 IK 解算
            float[] solvedJointAngles = kinematicsCalculator.SolveIK(currentPos, currentEuler, ikGuessJointAngles);
            
            if (solvedJointAngles != null && solvedJointAngles.Length == robotDOF)
            {
                trajectory.Add(solvedJointAngles);
                // 更新 IK 猜测值，供下一次迭代使用
                ikGuessJointAngles = (float[])solvedJointAngles.Clone(); 
            }
            else
            {
                Debug.LogWarning($"Path Planner (Placeholder): 笛卡尔路径点 {i} IK解算失败，轨迹可能不完整或不连续。当前位姿: {currentPos}, {currentEuler}", this);
                // 可以选择中断或继续，取决于错误处理策略。这里选择继续，但会跳过该点。
                // 为了确保轨迹长度一致，即使IK失败，也可能需要添加一个“上一个有效”的关节姿态，
                // 或者返回null/抛出异常，让MotionPlanner处理。当前是跳过。
            }
        }
        Debug.Log($"Path Planner (Placeholder): 生成了 {trajectory.Count} 个笛卡尔空间轨迹点。");
        return trajectory;
    }
}