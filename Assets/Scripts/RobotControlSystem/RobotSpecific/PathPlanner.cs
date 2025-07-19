// File: Assets/Scripts/RobotControlSystem/RobotSpecific/PathPlanner.cs
// Description: 机械臂的路径规划器，用于生成关节空间或笛卡尔空间的平滑轨迹。

using UnityEngine;
using System.Collections.Generic;
using System; // For Tuple
using System.Linq;


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
    public List<float[]> GenerateJointSpaceFiveOrderTrajectory(float[] startJointAngles, float[] targetJointAngles, float timeStep, float speedFactor,float totaltime)
    {

        if (startJointAngles == null || targetJointAngles == null || startJointAngles.Length != targetJointAngles.Length)
        {
            throw new ArgumentException("起始关节角度和目标关节角度不能为空，且长度必须相同。");
        }
        if (timeStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeStep), "时间步长必须大于0。");
        }
        if (speedFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speedFactor), "速度因子必须大于0。");
        }

        int numJoints = startJointAngles.Length;
        List<float[]> trajectory = new List<float[]>();

        // 设定一个基础总时间，然后通过speedFactor进行缩放
        float baseTotalTime = totaltime; // 基础总时间，单位秒
        float totalTime = baseTotalTime / speedFactor; 

        // 计算轨迹点数量
        int numPoints = (int)Math.Ceiling(totalTime / timeStep) + 1;

        // 对每个关节进行五次多项式规划
        for (int i = 0; i < numJoints; i++)
        {
            float q0 = startJointAngles[i];
            float qf = targetJointAngles[i];

            // 假设起始和终止速度、加速度均为0，以实现平滑启动和停止
            // q(t) = a0 + a1*t + a2*t^2 + a3*t^3 + a4*t^4 + a5*t^5
            // 当 q'(0)=q''(0)=q'(T)=q''(T)=0 时，系数简化为：
            // a0 = q0
            // a1 = 0
            // a2 = 0
            // a3 = 10 * (qf - q0) / T^3
            // a4 = -15 * (qf - q0) / T^4
            // a5 = 6 * (qf - q0) / T^5
            
            float T_cubed = totalTime * totalTime * totalTime;
            float T_fourth = T_cubed * totalTime;
            float T_fifth = T_fourth * totalTime;

            float a0 = q0;
            float a3 = 10 * (qf - q0) / T_cubed;
            float a4 = -15 * (qf - q0) / T_fourth;
            float a5 = 6 * (qf - q0) / T_fifth;

            // 生成轨迹点
            for (int j = 0; j < numPoints; j++)
            {
                float t = (j+1) * timeStep;
                if (t > totalTime) // 确保时间不超过总时长
                {
                    t = totalTime;
                }

                // 计算当前关节角度
                float currentJointAngle = a0 + a3 * t * t * t + a4 * t * t * t * t + a5 * t * t * t * t * t;

                // 如果是第一个关节的计算，初始化 trajectory 中的 float[] 数组
                if (i == 0)
                {
                    trajectory.Add(new float[numJoints]);
                }
                trajectory[j][i] = currentJointAngle;
            }
        }

        Debug.Log($"Path Planner (Placeholder): 生成了 {trajectory.Count} 个关节空间轨迹点。");
        return trajectory;
    }

    /// <summary>
    /// 笛卡尔空间直线路径规划器占位符。
    /// 实际中会在笛卡尔空间生成一条直线，然后每一步调用 IK 转换为关节角度。
    /// </summary>
    /// <param name="startPose">起始末端位姿 。</param>
    /// <param name="targetPosition">目标末端位置。</param>
    /// <param name="targetEulerAngles">目标末端欧拉角。</param>
    /// <param name="timeStep">每个轨迹点之间的时间间隔（秒）。</param>
    /// <param name="kinematicsCalculator">运动学计算器实例（用于IK）。</param>
    /// <param name="currentJointAngles">当前关节角度，作为每次IK解算的起始猜测值。</param> 
    /// <param name="speedFactor">轨迹执行的速度因子。</param>
    /// <returns>包含关节角度数组的轨迹点列表。</returns>
     public List<float[]> GenerateCartesianSpaceTrajectory(
        UnityEngine.Matrix4x4 startPose, 
        UnityEngine.Matrix4x4 targetPose, 
        float timeStep, 
        KinematicsCalculator kinematicsCalculator,  
        float[] currentJointAngles, 
        float speedFactor)
    {
        if (kinematicsCalculator == null)
        {
            Debug.LogError("Path Planner: KinematicsCalculator 引用为空，无法进行笛卡尔空间规划。", this);
            return new List<float[]>();
        }
        
        // 确保 kinematicsCalculator 的 DOF 与期望的机器人 DOF 匹配
        if (currentJointAngles == null || currentJointAngles.Length != kinematicsCalculator.robotDOF)
        {
            Debug.LogError($"Path Planner: 传入的当前关节角度数组为空或长度不匹配 DOF ({kinematicsCalculator.robotDOF})。请确保提供有效的初始猜测。", this);
            return new List<float[]>();
        }
        
        if (timeStep <= 0)
        {
            Debug.LogError("Path Planner: timeStep 必须大于 0。", this);
            return new List<float[]>();
        }

        if (speedFactor <= 0)
        {
            Debug.LogError("Path Planner: speedFactor 必须大于 0。", this);
            return new List<float[]>();
        }

        // 从Matrix4x4中提取位置和旋转
        Vector3 startPosition = startPose.GetColumn(3); // 获取平移部分
        Quaternion startRotation = startPose.rotation; // 获取旋转部分 (Unity 2017.1+ 或者自定义扩展方法)
        Vector3 startEulerAngles = startRotation.eulerAngles; // 将Quaternion转换为欧拉角

        Vector3 targetPosition = targetPose.GetColumn(3); // 获取平移部分
        Quaternion targetRotation = targetPose.rotation; // 获取旋转部分
        Vector3 targetEulerAngles = targetRotation.eulerAngles; // 将Quaternion转换为欧拉角

        Debug.Log($"Path Planner (Placeholder): 生成笛卡尔空间直线轨迹。从 Pos:{startPosition}, Euler:{startEulerAngles} 到 Pos:{targetPosition}, Euler:{targetEulerAngles}");
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
            // 使用 Quaternion.Slerp 进行球面线性插值，以实现更平滑的旋转过渡
            Vector3 currentPos = Vector3.Lerp(startPosition, targetPosition, t);
            // 这里我们用 Slerp 插值 Quaternion，然后转换为 EulerAngles 传给 IK，因为 Euler Angles 插值可能存在万向节锁等问题。
            Quaternion currentRot = Quaternion.Slerp(startRotation, targetRotation, t);
            Vector3 currentEuler = currentRot.eulerAngles;

            // 对每个中间位姿调用 IK 解算
            // 传递四元数旋转给IK会更稳定，如果你的IK支持的话
            float[] solvedJointAngles = kinematicsCalculator.SolveIK(currentPos, currentRot, ikGuessJointAngles); 
            
            if (solvedJointAngles != null && solvedJointAngles.Length == kinematicsCalculator.robotDOF)
            {
                trajectory.Add(solvedJointAngles);
                // 更新 IK 猜测值，供下一次迭代使用
                ikGuessJointAngles = (float[])solvedJointAngles.Clone(); 
            }
            else
            {
                Debug.LogWarning($"Path Planner (Placeholder): 笛卡尔路径点 {i} IK解算失败，轨迹可能不完整或不连续。当前位姿: Pos {currentPos}, Rot {currentEuler}", this);
                // 错误处理策略：
                // 1. 可以选择中断轨迹生成并返回已生成的点 (当前实现)
                // 2. 可以选择添加上一个成功的关节角度，以保持轨迹长度一致性，但会造成局部停顿或卡顿
                // trajectory.Add(ikGuessJointAngles); 
                // 3. 抛出异常，让上层调用者处理
            }
        }
        Debug.Log($"Path Planner (Placeholder): 生成了 {trajectory.Count} 个笛卡尔空间轨迹点。");
        return trajectory;
    }
}