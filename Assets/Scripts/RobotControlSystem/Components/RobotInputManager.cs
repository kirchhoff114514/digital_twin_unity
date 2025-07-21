// File: Assets/Scripts/RobotControlSystem/Components/RobotInputManager.cs
// Description: 处理UI输入，将用户交互转化为RobotControlIntent事件。

using UnityEngine;
using UnityEngine.UI; // 用于UI元素，如Slider, InputField, Toggle, Dropdown
using System; // 用于Action事件
using System.Linq; // 用于简化数组的空值检查和查找
using TMPro; // 用于 TextMeshPro 组件

/// <summary>
/// RobotInputManager 负责监听UI输入并将其封装为 RobotControlIntent。
/// 然后通过事件将此意图发布给 MotionPlanner。
/// </summary>
public class RobotInputManager : MonoBehaviour
{
    // --- 事件定义 ---
    /// <summary>
    /// 当用户输入准备好被处理时触发此事件。
    /// MotionPlanner 将订阅此事件。
    /// </summary>
    public event Action<RobotControlIntent> OnRobotControlIntentUpdated;

    // --- UI 引用（在 Inspector 中拖拽赋值） ---

    [Header("模式选择 UI")]
    [Tooltip("示教模式切换按钮。")]
    public Toggle jointTeachingModeToggle;
    [Tooltip("控制模式切换按钮。")]
    public Toggle taskControlModeToggle;
    [Tooltip("Play模式切换按钮。")]
    public Button playModeButton; // 单一按钮用于切换到Play模式

    [Header("示教模式 UI (JointSpaceTeaching)")]
    [Tooltip("机械臂所有关节的Slider数组。请按关节顺序赋值。")]
    public Slider[] jointSliders;
    [Tooltip("显示当前关节角度的Text或InputField数组。")]
    public TextMeshProUGUI[] jointAngleTexts;

    [Header("控制模式 UI (TaskControl)")]
    [Tooltip("末端目标位置 X 坐标输入框。")]
    public TMP_InputField targetPosXInput;
    [Tooltip("末端目标位置 Y 坐标输入框。")]
    public TMP_InputField targetPosYInput;
    [Tooltip("末端目标位置 Z 坐标输入框。")]
    public TMP_InputField targetPosZInput;

    [Tooltip("末端目标 Pitch 角度输入框。")]
    public TMP_InputField targetPitchInput;
    [Tooltip("末端目标 Roll 角度输入框。")]
    public TMP_InputField targetRollInput;
    // 如果你的机械臂末端姿态还需要Yaw，可以添加 targetYawInput

    [Tooltip("规划算法选择Dropdown。")]
    public TMP_Dropdown planningAlgorithmDropdown;
    [Tooltip("控制模式的执行按钮。")]
    public Button executeTaskButton;

    [Header("Play 模式 UI")]
    [Tooltip("在Play模式下，抓取物体后将物体放入指定区域的按钮。")]
    public Button placeButton;

    // --- 新增统一夹爪控制 UI 引用 ---
    [Header("末端夹爪控制")]
    [Tooltip("控制夹爪开/关的单一按钮。")]
    public Button toggleGripperButton;
    [Tooltip("显示夹爪当前状态的文本 (例如: '打开' / '关闭')。")]
    public TextMeshProUGUI gripperStatusText;


    // --- 内部变量 ---
    private ControlMode _currentSelectedMode = ControlMode.JointSpaceTeaching; // 默认启动模式
    private float[] _currentJointAngles = new float[5]; // 假设5个关节，根据你的机械臂DOF调整
    private bool _isGripperOpen = false; // 内部状态：夹爪当前是打开还是关闭

    [Header("Play 模式配置")]
    [Tooltip("键盘控制关节的速度。")]
    public float keyboardJointSpeed = 5.0f; // 关节每秒转动的度数

    [Tooltip("放置位置（Target Position）。")]
    public Vector3 placePosition = new Vector3(0.5f, 0.2f, 0.3f); // 示例放置位置
    [Tooltip("放置时的末端姿态（Euler Angles）。")]
    public Vector3 placeEulerAngles = new Vector3(0f, 90f, 0f); // 示例放置姿态

    [Tooltip("初始位置（Home Position）。")]
    public Vector3 homePosition = new Vector3(0.0f, 0.5f, 0.0f); // 示例初始位置
    [Tooltip("初始位置时的末端姿态（Euler Angles）。")]
    public Vector3 homeEulerAngles = new Vector3(0f, 0f, 0f); // 示例初始姿态


    void Awake()
    {
        // 初始化：检查并设置默认值，绑定UI事件。
        // 确保关节数量与内部数组匹配
        if (jointSliders != null && _currentJointAngles.Length != jointSliders.Length)
        {
            _currentJointAngles = new float[jointSliders.Length]; // 根据实际UI滑块数量调整DOF
            Debug.LogWarning($"RobotInputManager: 内部关节角度数组大小已调整为 {jointSliders.Length} 以匹配关节滑块数量。", this);
        }

        SetupModeToggles();
        SetupJointSliders();
        SetupTaskControlUI();
        SetupPlayModeUI(); // 初始化Play模式UI
        SetupGripperControlUI(); // 初始化夹爪控制UI

        Debug.Log("RobotInputManager: 初始化完成，准备接收UI输入。");
    }

    void Update()
    {
        // 在Play模式下处理键盘输入
        if (_currentSelectedMode == ControlMode.PlayMode)
        {
            HandleKeyboardInput();
        }
    }

    /// <summary>
    /// 处理键盘输入以控制机械臂关节。
    /// </summary>
    private void HandleKeyboardInput()
    {
        bool jointChanged = false;
        float[] newJointAngles = (float[])_currentJointAngles.Clone(); // 克隆当前关节角度，避免直接修改

        // 示例：使用QE ASDW RF键控制5个关节
        // 可以根据实际需求调整键位和关节映射
        // Joint 1
        if (Input.GetKey(KeyCode.Q)) { newJointAngles[0] += keyboardJointSpeed * Time.deltaTime; jointChanged = true; }
        if (Input.GetKey(KeyCode.E)) { newJointAngles[0] -= keyboardJointSpeed * Time.deltaTime; jointChanged = true; }

        // Joint 2
        if (Input.GetKey(KeyCode.A)) { newJointAngles[1] += keyboardJointSpeed * Time.deltaTime; jointChanged = true; }
        if (Input.GetKey(KeyCode.D)) { newJointAngles[1] -= keyboardJointSpeed * Time.deltaTime; jointChanged = true; }

        // Joint 3
        if (Input.GetKey(KeyCode.S)) { newJointAngles[2] += keyboardJointSpeed * Time.deltaTime; jointChanged = true; }
        if (Input.GetKey(KeyCode.W)) { newJointAngles[2] -= keyboardJointSpeed * Time.deltaTime; jointChanged = true; }

        // Joint 4
        if (Input.GetKey(KeyCode.R)) { newJointAngles[3] += keyboardJointSpeed * Time.deltaTime; jointChanged = true; }
        if (Input.GetKey(KeyCode.F)) { newJointAngles[3] -= keyboardJointSpeed * Time.deltaTime; jointChanged = true; }

        // Joint 5 (如果你的机械臂有5个自由度)
        if (Input.GetKey(KeyCode.Z)) { newJointAngles[4] += keyboardJointSpeed * Time.deltaTime; jointChanged = true; }
        if (Input.GetKey(KeyCode.X)) { newJointAngles[4] -= keyboardJointSpeed * Time.deltaTime; jointChanged = true; }


        if (jointChanged)
        {
            // 限制关节角度在合理范围内（例如：-180到180度）
            for (int i = 0; i < newJointAngles.Length; i++)
            {
                newJointAngles[i] = Mathf.Clamp(newJointAngles[i], -180f, 180f); // 根据你的机械臂关节限制调整
            }

            _currentJointAngles = newJointAngles; // 更新内部状态
            // 发布 JointSpaceTeaching 意图，因为键盘控制也是直接改变关节角度
            OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateJointSpaceTeachingIntent(_currentJointAngles));
            // Debug.Log($"RobotInputManager: 键盘控制，发布关节意图: {_currentJointAngles[0].ToString("F1")}, {_currentJointAngles[1].ToString("F1")}, ...");

        }
    }


    /// <summary>
    /// 设置模式切换按钮的监听器。
    /// </summary>
    private void SetupModeToggles()
    {
        // 绑定模式切换Toggle事件
        if (jointTeachingModeToggle != null)
        {
            jointTeachingModeToggle.onValueChanged.AddListener(isOn => OnModeToggleChanged(ControlMode.JointSpaceTeaching, isOn));
        }
        if (taskControlModeToggle != null)
        {
            taskControlModeToggle.onValueChanged.AddListener(isOn => OnModeToggleChanged(ControlMode.TaskControl, isOn));
        }
        // Play模式按钮只在点击时激活PlayMode，不使用Toggle
        if (playModeButton != null)
        {
            playModeButton.onClick.AddListener(OnPlayModeButtonClicked);
        }

        // 确定初始模式并更新UI可见性
        if (jointTeachingModeToggle != null && jointTeachingModeToggle.isOn)
        {
            _currentSelectedMode = ControlMode.JointSpaceTeaching;
        }
        else if (taskControlModeToggle != null && taskControlModeToggle.isOn)
        {
            _currentSelectedMode = ControlMode.TaskControl;
        }
        else // 如果都没有默认选中，则默认选中示教模式
        {
            if (jointTeachingModeToggle != null) jointTeachingModeToggle.isOn = true;
            _currentSelectedMode = ControlMode.JointSpaceTeaching;
        }
        UpdateUIVisibility();
    }

    /// <summary>
    /// 模式切换时调用，更新当前模式并刷新UI。
    /// </summary>
    private void OnModeToggleChanged(ControlMode mode, bool isOn)
    {
        if (isOn)
        {
            _currentSelectedMode = mode;
            Debug.Log($"RobotInputManager: 模式切换到: {_currentSelectedMode}");
            UpdateUIVisibility();
        }
    }

    /// <summary>
    /// Play模式按钮点击时调用，切换到Play模式。
    /// </summary>
    private void OnPlayModeButtonClicked()
    {
        // 当点击Play模式按钮时，确保其他Toggle被关闭
        if (jointTeachingModeToggle != null) jointTeachingModeToggle.isOn = false;
        if (taskControlModeToggle != null) taskControlModeToggle.isOn = false;

        _currentSelectedMode = ControlMode.PlayMode;
        Debug.Log($"RobotInputManager: 模式切换到: {_currentSelectedMode}");
        UpdateUIVisibility();
    }


    /// <summary>
    /// 根据当前选择的模式，更新UI元素的可见性。
    /// 夹爪控制UI现在是独立的，不随模式切换隐藏/显示。
    /// </summary>
    private void UpdateUIVisibility()
    {
        // 隐藏所有模式的父面板
        SetPanelActive(GetPanelParent(jointSliders), false); // 示教模式面板
        SetPanelActive(GetPanelParent(targetPosXInput), false); // 控制模式面板
        SetPanelActive(GetPanelParent(placeButton), false); // Play模式面板

        switch (_currentSelectedMode)
        {
            case ControlMode.JointSpaceTeaching:
                SetPanelActive(GetPanelParent(jointSliders), true);
                break;
            case ControlMode.TaskControl:
                SetPanelActive(GetPanelParent(targetPosXInput), true);
                break;
            case ControlMode.PlayMode:
                // 在Play模式下，只显示“Place”按钮
                SetPanelActive(GetPanelParent(placeButton), true);
                break;
            case ControlMode.GripperControl:
                // 此时不显示任何特定模式的UI，通常由其他UI触发夹爪控制
                break;
        }
        // 夹爪控制UI现在始终可见（如果它在单独的面板中）或者由其自身状态控制。
        // 这里不需要根据模式来设置夹爪UI的可见性。
    }

    /// <summary>
    /// 获取UI元素的父面板GameObject。
    /// 可以传入单个UI元素，或UI元素数组的第一个有效元素作为参考。
    /// </summary>
    /// <param name="uiElement">单个UI元素（如 InputField）。</param>
    private GameObject GetPanelParent(MonoBehaviour uiElement)
    {
        if (uiElement != null && uiElement.transform.parent != null)
        {
            // 假设UI元素被直接放置在一个代表面板的GameObject下
            return uiElement.transform.parent.gameObject;
        }
        return null;
    }

    /// <summary>
    /// 获取UI元素数组的父面板GameObject。
    /// 使用数组中的第一个有效元素作为参考。
    /// </summary>
    /// <param name="uiElements">UI元素数组（如 Slider[]）。</param>
    private GameObject GetPanelParent(MonoBehaviour[] uiElements)
    {
        if (uiElements != null && uiElements.Length > 0)
        {
            // 查找数组中第一个非空的UI元素作为参考
            MonoBehaviour firstValidElement = uiElements.FirstOrDefault(e => e != null);
            if (firstValidElement != null)
            {
                return GetPanelParent(firstValidElement); // 调用单元素版本获取父级
            }
        }
        return null;
    }

    /// <summary>
    /// 设置一个GameObject及其子对象是否激活。
    /// </summary>
    private void SetPanelActive(GameObject panel, bool isActive)
    {
        if (panel != null)
        {
            panel.SetActive(isActive);
        }
    }


    /// <summary>
    /// 设置关节滑块的监听器和初始值。
    /// </summary>
    private void SetupJointSliders()
    {
        if (jointSliders == null || jointSliders.Length == 0) return;

        for (int i = 0; i < jointSliders.Length; i++)
        {
            int jointIndex = i; // 捕获循环变量
            if (jointSliders[jointIndex] != null)
            {
                jointSliders[jointIndex].onValueChanged.AddListener(value => OnJointSliderChanged(jointIndex, value));

                // 初始化当前关节角度数组
                _currentJointAngles[jointIndex] = jointSliders[jointIndex].value;

                // 更新显示文本
                if (jointAngleTexts != null && jointAngleTexts.Length > jointIndex && jointAngleTexts[jointIndex] != null)
                {
                    jointAngleTexts[jointIndex].text = jointSliders[jointIndex].value.ToString("F1") + "°";
                }
            }
        }
    }

    /// <summary>
    /// 关节滑块值改变时调用，发布 JointSpaceTeaching 意图。
    /// 只在示教模式下响应滑块变化。
    /// </summary>
    private void OnJointSliderChanged(int jointIndex, float value)
    {
        if (_currentSelectedMode == ControlMode.JointSpaceTeaching)
        {
            _currentJointAngles[jointIndex] = value;
            if (jointAngleTexts != null && jointAngleTexts.Length > jointIndex && jointAngleTexts[jointIndex] != null)
            {
                jointAngleTexts[jointIndex].text = value.ToString("F1") + "°";
            }
            // 实时发布意图，不再包含夹爪开合度，夹爪通过独立意图控制
            OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateJointSpaceTeachingIntent(_currentJointAngles));
        }
    }

    /// <summary>
    /// 设置控制模式UI的监听器。
    /// </summary>
    private void SetupTaskControlUI()
    {

        // 绑定执行按钮事件
        if (executeTaskButton != null)
        {
            executeTaskButton.onClick.AddListener(OnExecuteTaskButtonClicked);
        }
    }

    /// <summary>
    /// 设置夹爪控制UI的监听器和初始值。
    /// </summary>
    private void SetupGripperControlUI()
    {
        if (toggleGripperButton != null)
        {
            toggleGripperButton.onClick.AddListener(OnToggleGripperButtonClicked);
        }
        // 初始化夹爪状态显示
        UpdateGripperStatusText();
    }

    /// <summary>
    /// 统一的夹爪控制按钮点击时调用。
    /// </summary>
    private void OnToggleGripperButtonClicked()
    {
        _isGripperOpen = !_isGripperOpen; // 切换夹爪状态

        GripperState desiredState = _isGripperOpen ? GripperState.Open : GripperState.Close;

        // 发布独立的夹爪控制意图
        OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateGripperControlIntent(desiredState));
        Debug.Log($"RobotInputManager: 发布独立的夹爪控制意图: {(desiredState == GripperState.Open ? "打开" : "关闭")}");

        // 更新UI文本显示
        UpdateGripperStatusText();
    }

    /// <summary>
    /// 更新夹爪状态文本显示。
    /// </summary>
    private void UpdateGripperStatusText()
    {
        if (gripperStatusText != null)
        {
            gripperStatusText.text = _isGripperOpen ? "open" : "close";
        }
    }


    /// <summary>
    /// 执行任务按钮点击时调用，发布 TaskControl 意图。
    /// </summary>
    private void OnExecuteTaskButtonClicked()
    {
        if (_currentSelectedMode == ControlMode.TaskControl)
        {
            // 收集目标位置和欧拉角
            Vector3 targetPos = new Vector3(
                ParseInput(targetPosXInput),
                ParseInput(targetPosYInput),
                ParseInput(targetPosZInput)
            );
            Vector3 targetEuler = new Vector3(
                ParseInput(targetPitchInput),
                ParseInput(targetRollInput), // 修正：这里应该是targetRollInput
                0 // 假设Yaw为0或通过其他Input设定，此处未提供Yaw输入框
            );

            // 发布 TaskControl 意图 (不再包含夹爪状态，因为夹爪通过独立按钮触发)
            OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateTaskControlIntent(targetPos, targetEuler));
            Debug.Log($"RobotInputManager: 发布 TaskControl 意图。目标位置: {targetPos}, 欧拉角: {targetEuler}");
        }
        else
        {
            Debug.LogWarning("RobotInputManager: 当前不在控制模式，无法执行任务。", this);
        }
    }

    /// <summary>
    /// 解析 InputField 的文本为浮点数。
    /// </summary>
    private float ParseInput(TMP_InputField inputField)
    {
        if (inputField != null && float.TryParse(inputField.text, out float result))
        {
            return result;
        }
        // 如果InputField为空或解析失败，返回0，并发出警告（根据需求决定是否警告）
        if (inputField == null) Debug.LogWarning("RobotInputManager: 输入框引用为空，返回0。", this);
        else Debug.LogWarning($"RobotInputManager: 无法解析输入框 '{inputField.name}' 的文本 '{inputField.text}' 为浮点数，返回0。", this);
        return 0f;
    }

    /// <summary>
    /// 设置Play模式UI的监听器。
    /// </summary>
    private void SetupPlayModeUI()
    {
        if (placeButton != null)
        {
            placeButton.onClick.AddListener(OnPlaceButtonClicked);
        }
    }

    /// <summary>
    /// "Place"按钮点击时调用，发布 TaskControl 意图到放置位置，然后回到初始位置。
    /// </summary>
    private void OnPlaceButtonClicked()
    {
        if (_currentSelectedMode == ControlMode.PlayMode)
        {
            // 1. 发布到放置位置的意图
            // 假设使用默认的InverseKinematics算法到达指定位置
            OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateTaskControlIntent(placePosition, placeEulerAngles));
            Debug.Log($"RobotInputManager: 在Play模式下，发布放置意图。目标位置: {placePosition}, 欧拉角: {placeEulerAngles}");

            // 2. 接着发布回到初始位置的意图
            // 可以在MotionPlanner中实现串行执行，或者在这里延迟调用
            // 为了简化，这里直接发送两个意图，MotionPlanner需要处理序列
            OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateTaskControlIntent(homePosition, homeEulerAngles));
            Debug.Log($"RobotInputManager: 在Play模式下，发布回到初始位置意图。目标位置: {homePosition}, 欧拉角: {homeEulerAngles}");
        }
        else
        {
            Debug.LogWarning("RobotInputManager: 当前不在Play模式，无法执行放置操作。", this);
        }
    }
}