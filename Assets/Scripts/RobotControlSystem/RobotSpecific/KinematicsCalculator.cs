// File: Assets/Scripts/RobotControlSystem/RobotSpecific/KinematicsCalculator.cs
// Description: 负责所有机械臂运动学相关计算的通用解算器（正运动学、逆运动学、雅可比等）。

using UnityEngine;
using System; // For Tuple
using System.Collections.Generic; // For List

/// <summary>
/// 机械臂运动学计算器。
/// 负责正运动学 (FK)、逆运动学 (IK) 等与机械臂几何运动相关的计算。
/// </summary>
public class KinematicsCalculator : MonoBehaviour
{
    [Tooltip("机械臂的自由度数量。例如，5轴机械臂就是5。")]
    public int robotDOF = 5;

    // TODO: 未来需要在这里定义机械臂的连杆参数（DH参数或URDF模型数据）。
    // 这些参数是进行精确运动学计算的基础。
    // public List<float> linkLengths; 
    // public List<float> linkOffsets;
    // public List<float> jointMinLimits;
    // public List<float> jointMaxLimits;


    // --- 逆运动学 (Inverse Kinematics - IK) ---
    /// <summary>
    /// 逆运动学解算器占位符：将笛卡尔空间的目标位姿转换为关节空间角度。
    /// 在实际应用中，这里将实现复杂的数值解法（如牛顿法、CCD）或解析解法。
    /// </summary>
    /// <param name="targetPosition">末端目标位置 (Unity世界坐标或机械臂基座坐标系)。</param>
    /// <param name="targetEulerAngles">末端目标欧拉角 (Pitch, Roll, Yaw)。</param>
    /// <param name="currentJointAngles">当前的关节角度，作为IK解算的起始点（对于数值IK很重要）。</param>
    /// <returns>解算出的关节角度数组。如果无法达到或解算失败，返回null或空数组。</returns>
    public float[] SolveIK(Vector3 targetPosition, Vector3 targetEulerAngles, float[] currentJointAngles)
    {
        Debug.Log($"KinematicsCalculator (IK Placeholder): 尝试解算 P:{targetPosition}, E:{targetEulerAngles}");

        // --- 这是一个占位符实现 ---
        // 实际的IK解算是一个复杂的话题，需要根据你的机械臂连杆参数和类型（如串联、并联）进行。
        // 这里我们返回一组简单的假数据作为目标关节角度，以便测试流程。
        // currentJointAngles 参数对于数值IK解算非常重要，因为它提供了迭代的起始点，
        // 帮助找到最接近当前状态的解，避免不必要的“跳变”。
        
        // 确保 currentJointAngles 的长度与 DOF 匹配
        if (currentJointAngles == null || currentJointAngles.Length != robotDOF)
        {
            currentJointAngles = new float[robotDOF]; // 如果不匹配或为空，则初始化为零
            Debug.LogWarning("KinematicsCalculator: IK初始猜测关节数组为空或长度不匹配，已初始化为零。", this);
        }

        float[] solvedJointAngles = new float[robotDOF];

        // 模拟：简单地将X位置映射到第一个关节，Y位置映射到第二个，等等。
        // 并结合当前关节角度进行一些“平滑”模拟，使其看起来像基于当前状态的迭代。
        // 这只是为了让系统能跑起来，并不是真实的IK。
        // 实际的IK会考虑整个机械臂的几何结构。
        if (robotDOF >= 1) solvedJointAngles[0] = Mathf.Lerp(currentJointAngles[0], targetPosition.x * 10f, 0.5f);
        if (robotDOF >= 2) solvedJointAngles[1] = Mathf.Lerp(currentJointAngles[1], targetPosition.y * 20f, 0.5f);
        if (robotDOF >= 3) solvedJointAngles[2] = Mathf.Lerp(currentJointAngles[2], targetPosition.z * 15f, 0.5f);
        if (robotDOF >= 4) solvedJointAngles[3] = Mathf.Lerp(currentJointAngles[3], targetEulerAngles.x * 2f, 0.5f);
        if (robotDOF >= 5) solvedJointAngles[4] = Mathf.Lerp(currentJointAngles[4], targetEulerAngles.y * 2f, 0.5f);
        if (robotDOF >= 6) solvedJointAngles[5] = Mathf.Lerp(currentJointAngles[5], targetEulerAngles.z * 2f, 0.5f); // 如果有第六个关节（Yaw）

        // 确保角度在合理范围内，例如-180到180度
        for(int i = 0; i < solvedJointAngles.Length; i++)
        {
            solvedJointAngles[i] = NormalizeAngle(solvedJointAngles[i]);
        }

        Debug.Log($"KinematicsCalculator (IK Placeholder): 解算结果 (模拟): {string.Join(", ", System.Linq.Enumerable.Select(solvedJointAngles, a => a.ToString("F1")))}");
        return solvedJointAngles;
    }

    // --- 正运动学 (Forward Kinematics - FK) ---
    /// <summary>
    /// 正运动学解算器占位符：根据关节角度计算末端执行器的位姿（位置和欧拉角）。
    /// </summary>
    /// <param name="jointAngles">所有关节的当前角度。</param>
    /// <returns>末端执行器的位姿，包含位置 (Vector3) 和欧拉角 (Vector3)。</returns>
    public Tuple<Vector3, Vector3> SolveFK(float[] jointAngles)
    {
        if (jointAngles == null || jointAngles.Length != robotDOF)
        {
            Debug.LogError("KinematicsCalculator (FK Placeholder): 关节角度数组为空或长度不匹配 DOF。", this);
            return new Tuple<Vector3, Vector3>(Vector3.zero, Vector3.zero);
        }

        Debug.Log($"KinematicsCalculator (FK Placeholder): 计算FK，关节角度: {string.Join(", ", System.Linq.Enumerable.Select(jointAngles, a => a.ToString("F1")))}");

        // --- 这是一个占位符实现 ---
        // 实际的FK解算需要基于DH参数或机械臂的几何结构，逐个连杆进行坐标变换。
        // 这里我们返回一个简单的模拟值，与关节角度线性相关。
        Vector3 simulatedPosition = new Vector3(
            jointAngles[0] * 0.01f + jointAngles[2] * 0.005f, // 模拟关节角度与位置的关系
            0.5f + jointAngles[1] * 0.02f - jointAngles[3] * 0.003f,
            jointAngles[2] * 0.015f + jointAngles[4] * 0.002f
        );
        Vector3 simulatedEulerAngles = new Vector3(
            jointAngles[3] * 0.5f,
            jointAngles[4] * 0.5f,
            0f // 假设Yaw为0，或根据robotDOF添加第六个关节
        );

        Debug.Log($"KinematicsCalculator (FK Placeholder): FK解算结果 (模拟): Pos:{simulatedPosition}, Euler:{simulatedEulerAngles}");
        return new Tuple<Vector3, Vector3>(simulatedPosition, simulatedEulerAngles);
    }

    // --- 雅可比矩阵 (Jacobian Matrix) ---
    /// <summary>
    /// 雅可比矩阵计算占位符：计算机械臂在当前关节构型下的雅可比矩阵。
    /// 雅可比矩阵将关节空间的速度映射到笛卡尔空间的速度。
    /// </summary>
    /// <param name="jointAngles">所有关节的当前角度。</param>
    /// <returns>表示雅可比矩阵的二维浮点数组。</returns>
    public float[,] CalculateJacobian(float[] jointAngles)
    {
        if (jointAngles == null || jointAngles.Length != robotDOF)
        {
            Debug.LogError("KinematicsCalculator (Jacobian Placeholder): 关节角度数组为空或长度不匹配 DOF。", this);
            return null;
        }

        Debug.Log($"KinematicsCalculator (Jacobian Placeholder): 计算雅可比矩阵，关节角度: {string.Join(", ", System.Linq.Enumerable.Select(jointAngles, a => a.ToString("F1")))}");

        // --- 这是一个占位符实现 ---
        // 实际的雅可比矩阵计算是一个复杂的数学过程，涉及连杆的几何关系和偏导数。
        // 对于一个 N 自由度机械臂，雅可比矩阵通常是 6xN 或 3xN 的。
        // 这里我们返回一个简化的虚拟矩阵。
        // 假设输出是 6xDOF (位置和方向)
        float[,] jacobian = new float[6, robotDOF]; 

        // 填充一些模拟值
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < robotDOF; c++)
            {
                // 确保模拟值不会太离谱，且与关节角度有关联
                jacobian[r, c] = (float)Math.Sin(jointAngles[c] * Mathf.Deg2Rad * 0.1f + r) * 0.1f + UnityEngine.Random.Range(-0.01f, 0.01f); 
            }
        }
        
        // 实际输出会更复杂，例如：
        // J = [ Jv ]  <- 线性速度雅可比
        //     [ Jw ]  <- 角速度雅可比

        Debug.Log("KinematicsCalculator (Jacobian Placeholder): 雅可比矩阵计算完毕 (模拟)。");
        return jacobian;
    }

    /// <summary>
    /// 规范化角度到 -180 到 180 度之间。
    /// Unity的EulerAngles可能返回0-360度。
    /// </summary>
    /// <param name="angle"></param>
    /// <returns></returns>
    private float NormalizeAngle(float angle)
    {
        angle = angle % 360;
        if (angle > 180)
            angle -= 360;
        else if (angle < -180)
            angle += 360;
        return angle;
    }

    // TODO: 未来还可以添加其他运动学相关方法，例如：
    // - IsReachable(Vector3 targetPosition, Vector3 targetEulerAngles): 判断目标是否在工作空间内
    // - CheckCollision(float[] jointAngles): 碰撞检测
    // - GetJointLimits(): 获取关节的旋转限制
}