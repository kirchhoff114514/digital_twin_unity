// File: Assets/Scripts/RobotControlSystem/Components/RobotArmExecutor.cs
// Description: 根据下位机反馈的实际关节角度和夹爪开闭状态，更新数字孪生机械臂模型。
//              在Awake时将当前手动设置的关节位置作为新的零位基准。

using UnityEngine;
using System; // For Debug.Log (string interpolation)
using System.Linq; // For Debug.Log (string.Join)

// 确保 GripperState 枚举在此文件或可访问的命名空间中定义。
// 它应该已经从 RobotActualState.cs 或 RobotControlIntent.cs 中导入/定义。

/// <summary>
/// RobotArmExecutor 负责接收物理机械臂的实际关节角度和夹爪开闭状态，
/// 并更新数字孪生模型以进行可视化同步。
/// 它会将 Awake 时记录的关节姿态作为后续运动的零位基准。
/// </summary>
public class RobotArmExecutor : MonoBehaviour
{
    // --- 公共引用（Inspector 或 RobotControlSystemManager 注入） ---

    /// <summary>
    /// 虚拟机械臂所有可动关节的 Transform 数组。请确保按关节顺序赋值。
    /// </summary>
    [Tooltip("拖拽所有虚拟机械臂关节的 Transform 组件到此数组中，按顺序排列。")]
    public Transform[] robotJointTransforms;

    /// <summary>
    /// 虚拟机械臂每个关节的局部旋转轴。
    /// 确保数组顺序与 robotJointTransforms 对应。
    /// 例如：Vector3.up (0,1,0) 代表绕局部Y轴，Vector3.right (1,0,0) 代表绕局部X轴。
    /// </summary>
    [Tooltip("指定每个关节的局部旋转轴。顺序应与Robot Joint Transforms数组一致。")]
    public Vector3[] jointRotationAxes;

    /// <summary>
    /// 虚拟机械臂关节的平滑速度因子。数值越大，模型跟随实际角度的速度越快。
    /// </summary>
    [Tooltip("虚拟机械臂关节的平滑速度因子。数值越大，模型跟随实际角度的速度越快。")]
    [Range(1f, 100f)]
    public float jointLerpSpeed = 20f;

    // --- 夹爪相关引用和参数 ---
    /// <summary>
    /// 虚拟夹爪左侧部分的 Transform。
    /// </summary>
    [Tooltip("拖拽虚拟夹爪左侧部分的 Transform。")]
    public Transform gripperLeftTransform;
    /// <summary>
    /// 虚拟夹爪右侧部分的 Transform。
    /// </summary>
    [Tooltip("拖拽虚拟夹爪右侧部分的 Transform。")]
    public Transform gripperRightTransform;

    // 移除了 gripperClosedMotorAngle 和 gripperOpenMotorAngle。
    // 因为 SerialCommunicator 已将原始电机角度转换为 GripperState 枚举，
    // RobotArmExecutor 直接根据 GripperState 来设置虚拟模型的视觉角度。
    // 原始电机角度的定义现在应由 SerialCommunicator 或其他负责硬件通信的模块管理。

    /// <summary>
    /// 虚拟夹爪模型在完全闭合时，其局部旋转的欧拉角。
    /// 例如，如果夹爪闭合时是 0 度，张开时是 -45 度，这里就是 0。
    /// </summary>
    [Tooltip("虚拟夹爪模型在完全闭合时，其局部旋转的欧拉角。")]
    public float virtualGripperClosedVisualAngle = 0f;

    /// <summary>
    /// 虚拟夹爪模型在完全张开时，其局部旋转的欧拉角。
    /// 例如，如果夹爪闭合时是 0 度，张开时是 -45 度，这里就是 -45。
    /// </summary>
    [Tooltip("虚拟夹爪模型在完全张开时，其局部旋转的欧拉角。")]
    public float virtualGripperOpenVisualAngle = -45f; 

    /// <summary>
    /// 虚拟夹爪的平滑速度因子。数值越大，模型跟随实际角度的速度越快。
    /// </summary>
    [Tooltip("虚拟夹爪的平滑速度因子。数值越大，模型跟随实际角度的速度越快。")]
    [Range(1f, 100f)]
    public float gripperLerpSpeed = 20f;


    // --- 内部状态 ---
    // 用于存储每个关节在 Awake 时捕捉到的“自定义零位”旋转
    // 这是机械臂模型在 Unity Scene 中手动摆放的初始姿态
    private Quaternion[] _initialZeroRotations;

    // --- 用于通知 MotionPlanner 实际状态更新的事件 ---
    /// <summary>
    /// 当数字孪生模型根据下位机反馈更新后，触发此事件。
    /// 用于通知 MotionPlanner 当前的实际机械臂状态。
    /// </summary>
    public event Action<float[]> OnActualStateUpdated;

    // --- Unity 生命周期方法 ---

    void Awake()
    {
        // --- 初始化检查 ---
        if (robotJointTransforms == null || robotJointTransforms.Length == 0)
        {
            Debug.LogError("RobotArmExecutor: 'Robot Joint Transforms' 数组为空或未设置！无法启动。", this);
            enabled = false;
            return;
        }
        if (jointRotationAxes == null || jointRotationAxes.Length != robotJointTransforms.Length)
        {
            Debug.LogError("RobotArmExecutor: 'Joint Rotation Axes' 数组未设置或长度与关节数量不匹配！无法启动。", this);
            enabled = false;
            return;
        }

        // --- 记录当前（手动调整后）的关节姿态作为新的零位基准 ---
        _initialZeroRotations = new Quaternion[robotJointTransforms.Length];

        for (int i = 0; i < robotJointTransforms.Length; i++)
        {
            if (robotJointTransforms[i] != null)
            {
                // 记录每个关节在 Awake 时的局部旋转，这被视为该关节的“零位”或“初始校准位”
                _initialZeroRotations[i] = robotJointTransforms[i].localRotation;
                Debug.Log($"RobotArmExecutor: 关节 {robotJointTransforms[i].name} 的初始零位已记录为: {_initialZeroRotations[i].eulerAngles}");
            }
            else
            {
                Debug.LogWarning($"RobotArmExecutor: 关节 {i} 的 Transform 为空，无法记录零位！");
            }
        }

        Debug.Log("RobotArmExecutor: 初始化完成，当前关节姿态已记录为新的零位基准。等待实际状态反馈。");
    }

    void Update()
    {
        // 此处不再有主动的轨迹执行逻辑
        // 模型的更新将由 SerialCommunicator 通过 UpdateDigitalTwin 方法驱动
    }

    // --- 公共方法 ---

    /// <summary>
    /// **核心方法：根据下位机反馈的实际关节角度和夹爪开闭状态，更新数字孪生模型。**
    /// 这个方法应该由 SerialCommunicator 的 OnActualRobotStateReceived 事件调用。
    /// </summary>
    /// <param name="actualJointAngles">物理机械臂的实际关节角度数组（相对于物理零位）。</param>
    /// <param name="actualGripperState">物理夹爪的实际开闭状态（枚举值）。</param>
    public void UpdateDigitalTwin(float[] actualJointAngles, GripperState actualGripperState)
    {
        if (actualJointAngles == null || actualJointAngles.Length != robotJointTransforms.Length)
        {
            Debug.LogError("RobotArmExecutor: 接收到的实际关节角度数组长度不匹配关节 Transforms 数量。", this);
            return;
        }

        // --- 更新机械臂关节 ---
        for (int i = 0; i < robotJointTransforms.Length; i++)
        {
            Transform joint = robotJointTransforms[i];
            if (joint == null) continue; // 跳过空的关节引用

            float actualAngle = actualJointAngles[i]; // 这是来自下位机的实际角度，相对于物理零位
            Vector3 rotationAxis = jointRotationAxes[i]; // 当前关节的局部旋转轴

            // 1. 创建一个代表实际反馈角度的旋转 (围绕其指定轴)
            Quaternion angleFromActualFeedback = Quaternion.AngleAxis(actualAngle, rotationAxis);

            // 2. 将此旋转叠加到 Awake 时记录的“初始零位”上，得到数字孪生模型的目标局部旋转
            Quaternion targetLocalRotation = _initialZeroRotations[i] * angleFromActualFeedback;

            // 3. 平滑地将关节旋转到目标
            joint.localRotation = Quaternion.Lerp(
                joint.localRotation,
                targetLocalRotation,
                Time.deltaTime * jointLerpSpeed
            );
        }

        // --- 更新夹爪 ---
        if (gripperLeftTransform != null && gripperRightTransform != null)
        {
            float targetGripperVisualAngle;

            switch (actualGripperState)
            {
                case GripperState.Open:
                    targetGripperVisualAngle = virtualGripperOpenVisualAngle;
                    break;
                case GripperState.Close:
                    targetGripperVisualAngle = virtualGripperClosedVisualAngle;
                    break;
                case GripperState.None:
                default:
                    // 如果是 None 状态，保持当前夹爪视觉角度（或设置为一个默认的中间状态）
                    // 这里我们选择保持当前，这通常意味着夹爪在打开和关闭阈值之间，
                    // 或没有明确的开闭意图，因此模型应保持其当前位置。
                    targetGripperVisualAngle = (gripperLeftTransform.localEulerAngles.z + (-gripperRightTransform.localEulerAngles.z)) / 2f; 
                    break;
            }

            // 假设夹爪的两个部分对称运动 (左爪正向，右爪反向)
            // 你可能需要根据你的模型实际轴向和运动方向来调整这里
            Quaternion leftTargetRotation = Quaternion.Euler(gripperLeftTransform.localEulerAngles.x, gripperLeftTransform.localEulerAngles.y, targetGripperVisualAngle);
            Quaternion rightTargetRotation = Quaternion.Euler(gripperRightTransform.localEulerAngles.x, gripperRightTransform.localEulerAngles.y, -targetGripperVisualAngle); // 反向旋转

            gripperLeftTransform.localRotation = Quaternion.Lerp(gripperLeftTransform.localRotation, leftTargetRotation, Time.deltaTime * gripperLerpSpeed);
            gripperRightTransform.localRotation = Quaternion.Lerp(gripperRightTransform.localRotation, rightTargetRotation, Time.deltaTime * gripperLerpSpeed);
        }
        else if (gripperLeftTransform == null || gripperRightTransform == null)
        {
            Debug.LogWarning("RobotArmExecutor: 夹爪 Transform 未完全设置，夹爪模型无法同步。", this);
        }

        OnActualStateUpdated?.Invoke(actualJointAngles);

        // (可选) 在这里可以添加日志，显示当前数字孪生模型更新后的关节角度
        // Debug.Log($"RobotArmExecutor: 数字孪生模型已更新。实际关节角度: {string.Join(", ", actualJointAngles.Select(a => a.ToString("F1")))}°, 实际夹爪状态: {actualGripperState}");
    }
}