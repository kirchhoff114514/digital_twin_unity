// File: Assets/Scripts/RobotControlSystem/RobotSpecific/KinematicsCalculator.cs
// Description: 负责所有机械臂运动学相关计算的通用解算器（正运动学、逆运动学、雅可比等）。

using UnityEngine;
using System; // For Tuple, Math
using System.Collections.Generic; // For List
using System.Linq; // For LINQ operations like Select, Sum

/// <summary>
/// 机械臂运动学计算器。
/// 负责正运动学 (FK)、逆运动学 (IK) 等与机械臂几何运动相关的计算。
/// </summary>
public class KinematicsCalculator : MonoBehaviour
{
    [Tooltip("机械臂的自由度数量。此IK解算器是为5轴机械臂设计的。")]
    public int robotDOF = 5;

    [Header("机器人臂连杆参数 (根据您的实际机器人模型设置)")]
    // 这里的值将用于 IK，但对于 FK，我们将直接使用 DH 参数。
    public float l1 = 0.3f; 
    public float l2 = 0.3f;
    public float l3 = 0.1f;

    [Header("逆运动学 (IK) 设置")]
    [Tooltip("靠近基座的关节（索引较小）其角度变化成本的权重乘数。")]
    public float baseJointWeightMultiplier = 2.0f; 
    [Tooltip("每个关节的默认角度变化成本权重。权重会随关节索引增加而递减。")]
    public float jointWeightDecrementFactor = 0.5f;
    [Tooltip("IK 解算失败时的容差，例如当 arccos 的输入超出 [-1,1] 范围时。")]
    public float ikSolveTolerance = 0.001f;

    // --- 改进 DH 参数定义 ---
    // 每个 Link 都定义为一个 DH_Link 结构体
    // theta (初始值，会在FK中加上关节变量), d, a, alpha, offset
    public struct DH_Link
    {
        public float d;     // Link offset (沿 Z 轴平移)
        public float a;     // Link length (沿 X 轴平移)
        public float alpha; // Link twist (绕 X 轴旋转)
        public float offset;// Joint variable offset (实际关节角度 + offset 才是用于计算的值)

        public DH_Link(float d, float a, float alpha, float offset)
        {
            this.d = d;
            this.a = a;
            this.alpha = alpha;
            this.offset = offset;
        }
    }

    // 存储 DH 参数的数组
    private DH_Link[] dhParameters;

    void Awake()
    {
        // 在 Awake 时初始化 DH 参数
        // 注意：这里没有 L1 的 theta/d/a/alpha/offset 信息，假设它是基座关节。
        // L1=Link([0 0 0 0 0 0], 'modified'); 这里的 L1 看起来是一个“虚”的基座关节，其DH参数均为0，通常表示机器人基座到第一个运动关节的变换。
        // 实际的运动从 L2 开始，对应着关节0 (theta1) 的运动。
        // 在我们的数组中，索引 0 对应 L1，索引 1 对应 L2，以此类推。
        // 数组的长度应该和 robotDOF 匹配，即 5个关节。

        dhParameters = new DH_Link[robotDOF]; // 5个关节

        // L1 关节 (索引 0) - 通常是基座关节，DH参数全为0，表示世界坐标系到关节1（L2）的变换。
        // 这里根据你给的 L1=Link([0 0 0 0 0 0], 'modified');
        dhParameters[0] = new DH_Link(0f, 0f, 0f * Mathf.Deg2Rad, 0f * Mathf.Deg2Rad); // d, a, alpha(弧度), offset(弧度)

        // L2 关节 (索引 1) - 对应 theta1
        // L2=Link([0 pi/2 41.5 pi/2 0 pi/4], 'modified');
        // theta: 0 (会被关节变量代替), d: pi/2, a: 41.5, alpha: pi/2, offset: pi/4
        // 注意：你的 d 是 pi/2，这通常表示一个平移量。如果 pi/2 实际是指长度单位，请确认。
        // 我这里将 d 视为长度，将 alpha 和 offset 转换为弧度。
        dhParameters[1] = new DH_Link(Mathf.PI / 2f, 41.5f, Mathf.PI / 2f, Mathf.PI / 4f); 

        // L3 关节 (索引 2) - 对应 theta2
        // L3=Link([0 0 110 0 0 -pi/4], 'modified');
        // theta: 0, d: 0, a: 110, alpha: 0, offset: -pi/4
        dhParameters[2] = new DH_Link(0f, 110f, 0f, -Mathf.PI / 4f);

        // L4 关节 (索引 3) - 对应 theta3
        // L4=Link([0 0 110 0 0 pi/2], 'modified');
        // theta: 0, d: 0, a: 110, alpha: 0, offset: pi/2
        dhParameters[3] = new DH_Link(0f, 110f, 0f, Mathf.PI / 2f);

        // L5 关节 (索引 4) - 对应 theta4
        // L5=Link([0 150 0 pi/2 0 0], 'modified');
        // theta: 0, d: 150, a: 0, alpha: pi/2, offset: 0
        dhParameters[4] = new DH_Link(150f, 0f, Mathf.PI / 2f, 0f);
    }


    // --- 逆运动学 (Inverse Kinematics - IK) ---
    // (此处代码与之前的版本相同，保持不变)
    public float[] SolveIK(Vector3 targetPosition, Vector3 targetEulerAngles, float[] currentJointAngles)
    {
        Debug.Log($"KinematicsCalculator (IK): 尝试解算 P:{targetPosition}, E:{targetEulerAngles}");

        if (currentJointAngles == null || currentJointAngles.Length != robotDOF)
        {
            currentJointAngles = new float[robotDOF]; 
            Debug.LogWarning("KinematicsCalculator: IK 初始猜测关节数组为空或长度不匹配，已初始化为零。", this);
        }

        List<float[]> possibleSolutions = new List<float[]>();

        float theta5 = targetEulerAngles.y; 

        float x_ee = targetPosition.x;
        float y_ee = targetPosition.y;
        float z_ee = targetPosition.z;

        float a_prime = Mathf.Sqrt(x_ee * x_ee + y_ee * y_ee) - l3 * Mathf.Cos(targetEulerAngles.x * Mathf.Deg2Rad)-41.5f; // 重命名a为a_prime避免与DH参数中的a混淆
        float h_prime = z_ee - l3 * Mathf.Sin(targetEulerAngles.x * Mathf.Deg2Rad); // 重命名h为h_prime避免与DH参数中的h混淆

        float theta1 = Mathf.Atan2(y_ee, x_ee) * Mathf.Rad2Deg; 
        
        float r_ah_squared = a_prime * a_prime + h_prime * h_prime; 
        float cosTheta3Numerator = r_ah_squared - l1 * l1 - l2 * l2;
        float cosTheta3Denominator = 2 * l1 * l2;

        if (Mathf.Abs(cosTheta3Denominator) < 1e-6) 
        {
            Debug.LogError("KinematicsCalculator (IK): 连杆长度过小或为零，无法解算 Theta3。", this);
            return null;
        }

        float cosTheta3 = cosTheta3Numerator / cosTheta3Denominator;

        if (cosTheta3 > 1.0f + ikSolveTolerance || cosTheta3 < -1.0f - ikSolveTolerance)
        {
            Debug.LogWarning($"KinematicsCalculator (IK): 目标位置不可达或超出工作空间。cos(theta3) = {cosTheta3:F3}。");
            return null; 
        }
        
        cosTheta3 = Mathf.Clamp(cosTheta3, -1.0f, 1.0f);

        float theta3_solution1 = Mathf.Acos(cosTheta3) * Mathf.Rad2Deg; 
        float theta3_solution2 = -theta3_solution1; 

        foreach (float currentTheta3 in new float[] { theta3_solution1, theta3_solution2 })
        {
            float k1 = l1 + l2 * Mathf.Cos(currentTheta3 * Mathf.Deg2Rad); 
            float k2 = l2 * Mathf.Sin(currentTheta3 * Mathf.Deg2Rad); 

            if (Mathf.Abs(k1) < 1e-6 && Mathf.Abs(k2) < 1e-6)
            {
                Debug.LogWarning($"KinematicsCalculator (IK): k1 和 k2 都接近零，可能导致 Theta2 解算不准确 (对于 theta3={currentTheta3:F1}度)。");
            }

            float theta2 = (Mathf.Atan2(h_prime, a_prime) - Mathf.Atan2(k2, k1)) * Mathf.Rad2Deg;
            float theta4=targetEulerAngles.x-theta2-currentTheta3; 

            float[] solution = new float[robotDOF];
            solution[0] = theta1;
            solution[1] = theta2;
            solution[2] = currentTheta3;
            solution[3] = theta4;
            solution[4] = theta5;

            possibleSolutions.Add(solution);
        }
        
        if (possibleSolutions.Count == 0)
        {
            Debug.LogWarning("KinematicsCalculator (IK): 没有找到可行的 IK 解。");
            return null;
        }

        float[] bestSolution = null;
        float minCost = float.MaxValue;

        foreach (var solution in possibleSolutions)
        {
            float cost = CalculateWeightedCost(currentJointAngles, solution);
            if (cost < minCost)
            {
                minCost = cost;
                bestSolution = solution;
            }
        }

        bestSolution[1]-=45;
        bestSolution[2]+=45;
        Debug.Log($"KinematicsCalculator (IK): 解算结果 (已选择): {string.Join(", ", bestSolution.Select(a => a.ToString("F1")))}");

        return bestSolution;
    }

     /// <summary>
    /// 逆运动学解算函数重载。姿态输入为四元数。
    /// 此函数将四元数转换为欧拉角，然后调用接受欧拉角的 SolveIK 函数。
    /// </summary>
    /// <param name="targetPosition">目标末端执行器位置。</param>
    /// <param name="targetRotation">目标末端执行器旋转 (Quaternion)。</param>
    /// <param name="currentJointAngles">当前关节角度，作为IK解算的初始猜测值。</param>
    /// <returns>解算出的关节角度数组，如果无解则返回 null。</returns>
    public float[] SolveIK(Vector3 targetPosition, Quaternion targetRotation, float[] currentJointAngles)
    {
        // 将四元数转换为欧拉角
        // 注意：Quaternion.eulerAngles 返回的是围绕 Z、X、Y 轴的旋转，顺序可能与你机器人的欧拉角定义不同
        // 请确保这里的转换与你的机器人姿态表示方式一致
        Vector3 targetEulerAngles = targetRotation.eulerAngles;

        Debug.Log($"KinematicsCalculator (IK-Quaternion Overload): 尝试解算 P:{targetPosition}, Q:{targetRotation.eulerAngles} (Euler)");

        // 调用现有的 SolveIK 函数
        return SolveIK(targetPosition, targetEulerAngles, currentJointAngles);
    }

    /// <summary>
    /// 计算从当前关节角度到目标关节角度的加权成本。
    /// 越靠近基座的关节（索引较小），其角度变化量在成本计算中的权重越大。
    /// </summary>
    private float CalculateWeightedCost(float[] currentAngles, float[] targetAngles)
    {
        float totalCost = 0f;
        for (int i = 0; i < robotDOF; i++)
        {
            if (i >= currentAngles.Length || i >= targetAngles.Length)
            {
                Debug.LogWarning($"KinematicsCalculator: 成本计算时关节索引 {i} 超出范围。");
                continue;
            }

            float angleDiff = Mathf.Abs(NormalizeAngle(targetAngles[i] - currentAngles[i]));
            float weight = baseJointWeightMultiplier * (1f - (float)i * jointWeightDecrementFactor / Mathf.Max(1, robotDOF - 1));
            weight = Mathf.Max(0.1f, weight); 

            totalCost += angleDiff * weight;
        }
        return totalCost;
    }


    // --- 正运动学 (Forward Kinematics - FK) ---
    /// <summary>
    /// 正运动学解算器：根据关节角度计算末端执行器的齐次变换矩阵。
    /// 此实现基于改进 DH 参数的通用变换矩阵公式。
    /// </summary>
    /// <param name="jointAngles">所有关节的当前角度 (以度为单位)。</param>
    /// <returns>末端执行器的齐次变换矩阵 (Matrix4x4)。</returns>
    public Matrix4x4 SolveFK(float[] jointAngles)
    {
        if (jointAngles == null || jointAngles.Length != robotDOF)
        {
            Debug.LogError("KinematicsCalculator (FK): 关节角度数组为空或长度与 DOF 不匹配。", this);
            return Matrix4x4.identity; // 返回单位矩阵作为错误指示
        }

        Debug.Log($"KinematicsCalculator (FK): 计算 FK，关节角度: {string.Join(", ", jointAngles.Select(a => a.ToString("F1")))}");

        // 初始化总变换矩阵为单位矩阵
        Matrix4x4 totalTransform = Matrix4x4.identity;

        // 遍历所有关节，应用各自的 DH 变换
        for (int i = 0; i < robotDOF; i++)
        {
            // 获取当前关节的 DH 参数
            DH_Link currentLinkDH = dhParameters[i];

            // 计算实际用于变换的关节角度 (弧度)
            // 你的 jointAngles 是以度为单位，需要先转换为弧度
            float currentThetaRad = jointAngles[i] * Mathf.Deg2Rad; 
            // 加上 offset，才是 DH 参数中真正的 theta_i
            float actualDHTheta = currentThetaRad + currentLinkDH.offset; 

            // --- 构建改进 DH 参数的齐次变换矩阵 M_i-1_i 的通用形式 ---
            // [ cos(theta)          -sin(theta)cos(alpha)   sin(theta)sin(alpha)   a*cos(theta)  ]
            // [ sin(theta)          cos(theta)cos(alpha)    -cos(theta)sin(alpha)  a*sin(theta)  ]
            // [ 0                   sin(alpha)              cos(alpha)             d             ]
            // [ 0                   0                       0                      1             ]

            float cosTheta = Mathf.Cos(actualDHTheta);
            float sinTheta = Mathf.Sin(actualDHTheta);
            float cosAlpha = Mathf.Cos(currentLinkDH.alpha);
            float sinAlpha = Mathf.Sin(currentLinkDH.alpha);

            Matrix4x4 T_link = Matrix4x4.identity;
            T_link.m00 = cosTheta;
            T_link.m01 = -sinTheta * cosAlpha;
            T_link.m02 = sinTheta * sinAlpha;
            T_link.m03 = currentLinkDH.a * cosTheta; // X平移分量

            T_link.m10 = sinTheta;
            T_link.m11 = cosTheta * cosAlpha;
            T_link.m12 = -cosTheta * sinAlpha;
            T_link.m13 = currentLinkDH.a * sinTheta; // Y平移分量

            T_link.m20 = 0f;
            T_link.m21 = sinAlpha;
            T_link.m22 = cosAlpha;
            T_link.m23 = currentLinkDH.d; // Z平移分量

            T_link.m30 = 0f;
            T_link.m31 = 0f;
            T_link.m32 = 0f;
            T_link.m33 = 1f;

            // 将当前连杆的变换矩阵累乘到总变换矩阵
            totalTransform = totalTransform * T_link;
        }

        Debug.Log($"KinematicsCalculator (FK): FK 结果矩阵:\n{totalTransform}");
        return totalTransform;
    }

    // --- Jacobian Matrix ---
    // (此处代码与之前的版本相同，保持不变)
    public float[,] CalculateJacobian(float[] jointAngles)
    {
        if (jointAngles == null || jointAngles.Length != robotDOF)
        {
            Debug.LogError("KinematicsCalculator (雅可比矩阵 占位符): 关节角度数组为空或长度与 DOF 不匹配。", this);
            return null;
        }

        Debug.Log($"KinematicsCalculator (雅可比矩阵 占位符): 计算雅可比矩阵，关节角度: {string.Join(", ", jointAngles.Select(a => a.ToString("F1")))}");

        float[,] jacobian = new float[6, robotDOF]; 

        for (int r = 0; r < 6; r++)
            for (int c = 0; c < robotDOF; c++)
                jacobian[r, c] = (float)Math.Sin(jointAngles[c] * Mathf.Deg2Rad * 0.1f + r) * 0.1f + UnityEngine.Random.Range(-0.01f, 0.01f); 
        
        Debug.Log("KinematicsCalculator (雅可比矩阵 占位符): 雅可比矩阵计算完毕 (模拟)。");
        return jacobian;
    }

    /// <summary>
    /// 规范化角度到 -180 到 180 度之间。
    /// 用于处理角度差值时，确保选择最短的旋转路径。
    /// </summary>
    /// <param name="angle">待规范化的角度。</param>
    /// <returns>规范化后的角度。</returns>
    private float NormalizeAngle(float angle)
    {
        angle = angle % 360; 
        if (angle > 180)    
            angle -= 360;
        else if (angle < -180) 
            angle += 360;
        return angle;
    }
}