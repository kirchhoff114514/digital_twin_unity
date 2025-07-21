using UnityEngine;
using System; // For Debug.Log (string interpolation)
using System.Linq; // For Debug.Log (string.Join)

public class RobotArmExecutor : MonoBehaviour
{
    [Tooltip("拖拽所有虚拟机械臂关节的 Transform 组件到此数组中，按顺序排列。")]
    public Transform[] robotJointTransforms;

    [Tooltip("指定每个关节的局部旋转轴。顺序应与Robot Joint Transforms数组一致。")]
    public Vector3[] jointRotationAxes;

    [Tooltip("虚拟机械臂关节的平滑速度因子。数值越大，模型跟随实际角度的速度越快。")]
    [Range(1f, 100f)]
    public float jointLerpSpeed = 20f;

    [Tooltip("拖拽虚拟夹爪左侧部分的 Transform。")]
    public Transform gripperLeftTransform;

    [Tooltip("拖拽虚拟夹爪右侧部分的 Transform。")]
    public Transform gripperRightTransform;
    [Tooltip("夹爪转轴")]
    public Vector3 gripperRotationAxis = new Vector3(0, 1, 0);

    [Tooltip("虚拟夹爪模型在完全闭合时，其局部旋转的欧拉角。")]
    public float virtualGripperClosedVisualAngle = 0f;

    [Tooltip("虚拟夹爪模型在打开时，其局部旋转的欧拉角。")]
    public float virtualGripperOpenVisualAngle = 0f;

    [Tooltip("虚拟夹爪的平滑速度因子。数值越大，模型跟随实际角度的速度越快。")]
    [Range(1f, 100f)]
    public float gripperLerpSpeed = 20f;

    private Quaternion[] _initialZeroRotations;

    public float[] _currentJointAngles; // 存储当前关节角度
    public GripperState _currentGripperState; // 存储当前夹爪状态




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
        _initialZeroRotations = new Quaternion[robotJointTransforms.Length+2];

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
        _initialZeroRotations[robotJointTransforms.Length] = gripperLeftTransform.localRotation; // 夹爪左侧
        _initialZeroRotations[robotJointTransforms.Length + 1] = gripperRightTransform.localRotation; // 夹爪右侧

        Debug.Log("RobotArmExecutor: 初始化完成，当前关节姿态已记录为新的零位基准。等待实际状态反馈。");
    }

    // --- 公共方法 ---

    /// <summary>
    /// **核心方法：根据下位机反馈的实际关节角度和夹爪开闭状态，更新数字孪生模型。**
    /// 这个方法应该由 SerialCommunicator 的 OnActualRobotStateReceived 事件调用。
    /// </summary>
    /// <param name="JointAngles">物理机械臂的实际关节角度数组（相对于物理零位）。</param>
    /// <param name="GripperState">物理夹爪的实际开闭状态（枚举值）。</param>
    public void SetJointAngles(float[] JointAngles, GripperState GripperState)
    
    {

        _currentGripperState = GripperState; // 更新当前夹爪状态
        _currentJointAngles = JointAngles; // 更新当前关节角度
        if (JointAngles == null || JointAngles.Length != robotJointTransforms.Length)
        {
            Debug.LogError("RobotArmExecutor: 接收到的实际关节角度数组长度不匹配关节 Transforms 数量。", this);
            return;
        }

        // --- 更新机械臂关节 ---
        for (int i = 0; i < robotJointTransforms.Length; i++)
        {
            Transform joint = robotJointTransforms[i];
            if (joint == null) continue; // 跳过空的关节引用

            float actualAngle = JointAngles[i]; // 这是来自下位机的实际角度，相对于物理零位
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

            switch (GripperState)
            {
                case GripperState.Open:
                    targetGripperVisualAngle = virtualGripperOpenVisualAngle;
                    break;
                case GripperState.Close:
                    targetGripperVisualAngle = virtualGripperClosedVisualAngle;
                    break;
                case GripperState.None:
                default:

                    targetGripperVisualAngle = (gripperLeftTransform.localEulerAngles.z + (-gripperRightTransform.localEulerAngles.z)) / 2f; 
                    break;
            }

            // 假设夹爪的两个部分对称运动 (左爪正向，右爪反向)
            // 你可能需要根据你的模型实际轴向和运动方向来调整这里
            Quaternion angleFromActualFeedback = Quaternion.AngleAxis(targetGripperVisualAngle, gripperRotationAxis);
            Quaternion minus_angleFromActualFeedback = Quaternion.AngleAxis(-targetGripperVisualAngle, gripperRotationAxis);


            // 2. 将此旋转叠加到 Awake 时记录的“初始零位”上，得到数字孪生模型的目标局部旋转
            Quaternion left_targetLocalRotation = _initialZeroRotations[robotJointTransforms.Length] * angleFromActualFeedback;
            Quaternion right_targetLocalRotation = _initialZeroRotations[robotJointTransforms.Length+1] * minus_angleFromActualFeedback;

            // 3. 平滑地将关节旋转到目标
            gripperLeftTransform.localRotation = Quaternion.RotateTowards(gripperLeftTransform.localRotation,left_targetLocalRotation,Time.deltaTime * jointLerpSpeed);
            gripperRightTransform.localRotation = Quaternion.RotateTowards(gripperRightTransform.localRotation,right_targetLocalRotation,Time.deltaTime * jointLerpSpeed);
        }
        else if (gripperLeftTransform == null || gripperRightTransform == null)
        {
            Debug.LogWarning("RobotArmExecutor: 夹爪 Transform 未完全设置，夹爪模型无法同步。", this);
        }
    }
}