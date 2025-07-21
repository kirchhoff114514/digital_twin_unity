// File: Assets/Scripts/RobotControlSystem/RobotSpecific/PathPlanner.cs
// Description: 机械臂的路径规划器，仅用于计算五次多项式轨迹的系数。

using UnityEngine;
using System; // For ArgumentOutOfRangeException

public class PathPlanner : MonoBehaviour
{
    /// <summary>
    /// 计算五次多项式轨迹的系数。
    /// 假设起始和终止速度、加速度均为0，以实现平滑启动和停止。
    /// q(t) = a0 + a1*t + a2*t^2 + a3*t^3 + a4*t^4 + a5*t^5
    /// 当 q'(0)=q''(0)=q'(T)=q''(T)=0 时，系数简化为：
    /// a0 = q0
    /// a1 = 0
    /// a2 = 0
    /// a3 = 10 * (qf - q0) / T^3
    /// a4 = -15 * (qf - q0) / T^4
    /// a5 = 6 * (qf - q0) / T^5
    /// </summary>
    /// <param name="q0">起始位置（角度）。</param>
    /// <param name="qf">终止位置（角度）。</param>
    /// <param name="totalTime">总时间 T。</param>
    /// <returns>一个包含 [a0, a1, a2, a3, a4, a5] 的浮点数数组。</returns>
    public float[] CalculateFiveOrderPolynomialCoefficients(float q0, float qf, float totalTime)
    {
        if (totalTime <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalTime), "总时间必须大于0。");
        }

        float T_cubed = totalTime * totalTime * totalTime;
        float T_fourth = T_cubed * totalTime;
        float T_fifth = T_fourth * totalTime;

        float a0 = q0;
        float a1 = 0f; // 假设起始和终止速度为0
        float a2 = 0f; // 假设起始和终止加速度为0
        float a3 = 10 * (qf - q0) / T_cubed;
        float a4 = -15 * (qf - q0) / T_fourth;
        float a5 = 6 * (qf - q0) / T_fifth;

        return new float[] { a0, a1, a2, a3, a4, a5 };
    }
}