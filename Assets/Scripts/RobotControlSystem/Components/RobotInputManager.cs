using UnityEngine;
using UnityEngine.UI; // 用于UI元素，如Slider, InputField, Toggle, Dropdown
using System; // 用于Action事件
using System.Linq; // 用于简化数组的空值检查和查找
using TMPro; // 用于 TextMeshPro 组件


public class RobotInputManager : MonoBehaviour
{
    // Event for general robot control intents (e.g., keyboard input, slider changes)
    public event Action<RobotControlIntent> OnRobotControlIntentUpdated;
    // New events for standalone Reset and Place actions, which MotionPlanner will subscribe to

    // --- UI 引用（在 Inspector 中拖拽赋值） ---

    [Header("模式选择 UI")]
    [Tooltip("示教模式切换开关。")]
    public Toggle jointTeachingModeToggle;
    [Tooltip("控制模式切换开关。")]
    public Toggle taskControlModeToggle;
    [Tooltip("Play模式切换开关。")]
    public Toggle playModeToggle; // 将Button改为Toggle

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

    [Tooltip("控制模式的执行按钮。")]
    public Button executeTaskButton; // No longer needs planningAlgorithmDropdown

    [Header("独立自动化功能 UI")]
    [Tooltip("将机械臂复位到初始位置的按钮。")]
    public Button resetButton; // 新增：复位按钮
    [Tooltip("将物体放置到指定区域的按钮。")]
    public Button placeButton; // 现在是一个独立的自动化按钮

    [Header("末端夹爪控制")]
    [Tooltip("控制夹爪开/关的单一按钮。")]
    public Button toggleGripperButton;
    [Tooltip("显示夹爪当前状态的文本 (例如: '打开' / '关闭')。")]
    public TextMeshProUGUI gripperStatusText;
    


    // --- 内部变量 ---
    private ControlMode _currentSelectedMode = ControlMode.JointSpaceTeaching; // 默认启动模式
    private float[] _currentJointAngles = new float[5]; // 假设5个关节，根据你的机械臂DOF调整
    private bool _isGripperOpen = false; // 内部状态：夹爪当前是打开还是关闭

    // 用于跟踪当前末端执行器姿态（用于任务空间键盘控制）
    public Vector3 _currentEndEffectorPosition;
    public Vector3 _currentEndEffectorEulerAngles; // Yaw将保持固定为0，因为5自由度机械臂通常没有Yaw控制

    [Header("键盘任务空间控制配置 (PlayMode专属)")]
    [Tooltip("键盘控制XYZ位置的速度。单位：米/秒。")]
    public float keyboardTranslateSpeed = 0.1f; // 末端执行器每秒移动的米数
    [Tooltip("键盘控制Roll/Pitch角度的速度。单位：度/秒。")]
    public float keyboardRotateSpeed = 30.0f; // 末端执行器每秒旋转的度数

    [Header("自动化任务配置 (Reset/Place按钮使用)")]
    [Tooltip("放置位置（Target Position）。世界坐标。")]
    public Vector3 placePosition = new Vector3(0.5f, 0.2f, 0.3f); // 示例放置位置，请在Unity中调整
    [Tooltip("放置时的末端姿态（Euler Angles）。")]
    public Vector3 placeEulerAngles = new Vector3(0f, 90f, 0f); // 示例放置姿态，请在Unity中调整

    [Tooltip("初始位置（Home Position）。世界坐标。")]
    public Vector3 homePosition = new Vector3(0.0f, 0.5f, 0.0f); // 示例初始位置，请在Unity中调整
    [Tooltip("初始位置时的末端姿态（Euler Angles）。")]
    public Vector3 homeEulerAngles = new Vector3(0f, 0f, 0f); // 示例初始姿态，请在Unity中调整


    void Awake()
    {
        // Adjust joint angles array size based on provided sliders
        if (jointSliders != null && _currentJointAngles.Length != jointSliders.Length)
        {
            if (jointSliders.Length != 5)
            {
                Debug.LogWarning("RobotInputManager: Your robotic arm has 5 degrees of freedom (XYZ + Roll/Pitch), but the number of joint sliders is not 5. Please check the joint slider configuration or adjust the size of _currentJointAngles in the code.", this);
            }
            _currentJointAngles = new float[jointSliders.Length]; // Adjust DOF based on actual UI slider count
        }

        SetupModeToggles();
        SetupJointSliders();
        SetupTaskControlUI();
        SetupAutomationButtons(); // Renamed and consolidated automation button setup
        SetupGripperControlUI(); // Initialize gripper control UI

        // Initialize current end-effector pose to home position, as a starting point for task space keyboard control
        _currentEndEffectorPosition = homePosition;
        _currentEndEffectorEulerAngles = homeEulerAngles;

        Debug.Log("RobotInputManager: Initialization complete, ready to receive UI input.");
    }

    /// <summary>
    /// Unity Frame Update function, used to process keyboard input.
    /// </summary>
    void Update()
    {
        // Only process keyboard input when in PlayMode
        if (_currentSelectedMode == ControlMode.PlayMode)
        {
            HandleKeyboardInput();
        }
    }

    /// <summary>
    /// Handles keyboard input to control the robotic arm's end-effector XYZ position and Roll/Pitch orientation.
    /// </summary>
    private void HandleKeyboardInput()
    {
        // --- XYZ Movement Control ---
        Vector3 moveDelta = Vector3.zero;
        if (Input.GetKey(KeyCode.Y)) moveDelta += Vector3.forward; // Z-axis positive (forward)
        if (Input.GetKey(KeyCode.H)) moveDelta += Vector3.back;    // Z-axis negative (backward)
        if (Input.GetKey(KeyCode.K)) moveDelta += Vector3.left;    // X-axis negative (left)
        if (Input.GetKey(KeyCode.I)) moveDelta += Vector3.right;   // X-axis positive (right)
        if (Input.GetKey(KeyCode.J)) moveDelta += Vector3.up;  // Y-axis positive (up)
        if (Input.GetKey(KeyCode.L)) moveDelta += Vector3.down; // Y-axis negative (down)

        // --- Roll/Pitch Rotation Control ---
        Vector3 rotateDelta = Vector3.zero; // Unity EulerAngles order: X (Pitch), Y (Yaw), Z (Roll)
        // Pitch (around X-axis): Use Y/H
        if (Input.GetKey(KeyCode.M)) rotateDelta.x += 1; // Pitch up
        if (Input.GetKey(KeyCode.N)) rotateDelta.x -= 1; // Pitch down
        // Roll (around Z-axis): Use U/O
        if (Input.GetKey(KeyCode.U)) rotateDelta.y += 1; // Roll clockwise
        if (Input.GetKey(KeyCode.O)) rotateDelta.y -= 1; // Roll counter-clockwise

        bool inputDetected = false;

        // Calculate new position
        if (moveDelta != Vector3.zero)
        {
            _currentEndEffectorPosition += moveDelta.normalized * keyboardTranslateSpeed * Time.deltaTime;
            inputDetected = true;
        }

        // Calculate new orientation
        if (rotateDelta != Vector3.zero)
        {
            Vector3 newEulerAngles = _currentEndEffectorEulerAngles;
            newEulerAngles.x += rotateDelta.x * keyboardRotateSpeed * Time.deltaTime; // Pitch
            newEulerAngles.y += rotateDelta.y * keyboardRotateSpeed * Time.deltaTime; // Roll

            // For a 5-DOF robotic arm, Yaw (Y-axis) typically remains fixed at 0.
            newEulerAngles.z = 0f;

            // Ensure angles are within a reasonable range (e.g., -180 to 180 or 0 to 360)
            newEulerAngles.x = Mathf.Repeat(newEulerAngles.x + 180, 360) - 180; // Clamp Pitch to -180 to 180
            newEulerAngles.y = Mathf.Repeat(newEulerAngles.y + 180, 360) - 180; // Clamp Roll to -180 to 180

            _currentEndEffectorEulerAngles = newEulerAngles;
            inputDetected = true;
        }

        if (inputDetected)
        {
            // Publish a TaskControl intent, requesting MotionPlanner to reach the target pose using Inverse Kinematics

            OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreatePlayModeIntent(_currentEndEffectorPosition, _currentEndEffectorEulerAngles));
            Debug.Log($"RobotInputManager: Keyboard task space control, publishing intent. Target Position: {_currentEndEffectorPosition}, Euler Angles: {_currentEndEffectorEulerAngles}");
        }
    }


    /// <summary>
    /// Sets up listeners for mode selection toggles.
    /// </summary>
    private void SetupModeToggles()
    {
        // Get or add a ToggleGroup component to the current GameObject
        ToggleGroup modeToggleGroup = GetComponent<ToggleGroup>();
        if (modeToggleGroup == null)
        {
            modeToggleGroup = gameObject.AddComponent<ToggleGroup>();
        }

        // Bind Joint Teaching Mode Toggle event
        if (jointTeachingModeToggle != null)
        {
            jointTeachingModeToggle.group = modeToggleGroup; // Set Toggle's group
            jointTeachingModeToggle.onValueChanged.AddListener(isOn => OnModeToggleChanged(ControlMode.JointSpaceTeaching, isOn));
        }
        // Bind Task Control Mode Toggle event
        if (taskControlModeToggle != null)
        {
            taskControlModeToggle.group = modeToggleGroup; // Set Toggle's group
            taskControlModeToggle.onValueChanged.AddListener(isOn => OnModeToggleChanged(ControlMode.TaskControl, isOn));
        }
        // Bind Play Mode Toggle event
        if (playModeToggle != null)
        {
            playModeToggle.group = modeToggleGroup; // Set Toggle's group
            playModeToggle.onValueChanged.AddListener(isOn => OnModeToggleChanged(ControlMode.PlayMode, isOn));
        }

        // Determine initial mode and update UI visibility
        // By default, select Joint Teaching mode, which will trigger OnModeToggleChanged to set _currentSelectedMode
        if (jointTeachingModeToggle != null)
        {
            jointTeachingModeToggle.isOn = true;
        }
        else if (taskControlModeToggle != null)
        {
            taskControlModeToggle.isOn = true;
        }
        else if (playModeToggle != null)
        {
            playModeToggle.isOn = true;
        }
        // As a safety measure, if no Toggle in the ToggleGroup is selected (though ToggleGroup usually ensures at least one is selected)
        if (modeToggleGroup.AnyTogglesOn() == false)
        {
            _currentSelectedMode = ControlMode.JointSpaceTeaching; // Force default mode
            if (jointTeachingModeToggle != null) jointTeachingModeToggle.isOn = true; // Attempt to activate default Toggle
        }
        UpdateUIVisibility();
    }

    /// <summary>
    /// Called when a mode toggle's value changes, updates the current mode and refreshes the UI.
    /// </summary>
    private void OnModeToggleChanged(ControlMode mode, bool isOn)
    {
        // Only switch mode when the Toggle is selected.
        // Due to ToggleGroup behavior, when one Toggle is selected, others are automatically deselected,
        // and they will also trigger this method with isOn being false, which we ignore.
        if (isOn)
        {
            _currentSelectedMode = mode;
            if(mode==ControlMode.PlayMode){
                _currentEndEffectorPosition = homePosition; // Reset position to home when entering PlayMode
                _currentEndEffectorEulerAngles = homeEulerAngles; // Reset orientation to home when
            }
            Debug.Log($"RobotInputManager: Mode switched to: {_currentSelectedMode}");
            UpdateUIVisibility();
        }
    }

    /// <summary>
    /// Updates the visibility of UI elements based on the currently selected mode.
    /// Gripper control UI is now independent and doesn't hide/show with mode switching.
    /// Automation buttons (Reset/Place) are also always visible.
    /// </summary>
    private void UpdateUIVisibility()
    {
        SetPanelActive(GetPanelParent(jointSliders), false); // Joint Teaching Mode Panel
        SetPanelActive(GetPanelParent(targetPosXInput), false); // Task Control Mode Panel


        // Show panel for the current mode
        switch (_currentSelectedMode)
        {
            case ControlMode.JointSpaceTeaching:
                SetPanelActive(GetPanelParent(jointSliders), true);
                break;
            case ControlMode.TaskControl:
                SetPanelActive(GetPanelParent(targetPosXInput), true);
                break;
            case ControlMode.PlayMode:

                break;
            case ControlMode.GripperControl:

                break;
            case ControlMode.None:
                break;
        }

    }

    /// <summary>
    /// Gets the parent panel GameObject of a UI element.
    /// Can pass a single UI element, or the first valid element of a UI element array as reference.
    /// </summary>
    /// <param name="uiElement">Single UI element (e.g., InputField).</param>
    private GameObject GetPanelParent(MonoBehaviour uiElement)
    {
        if (uiElement != null && uiElement.transform.parent != null)
        {
            // Assumes the UI element is directly placed under a GameObject representing a panel
            return uiElement.transform.parent.gameObject;
        }
        return null;
    }

    /// <summary>
    /// Gets the parent panel GameObject of a UI element array.
    /// Uses the first valid element in the array as reference.
    /// </summary>
    /// <param name="uiElements">UI element array (e.g., Slider[]).</param>
    private GameObject GetPanelParent(MonoBehaviour[] uiElements)
    {
        if (uiElements != null && uiElements.Length > 0)
        {
            // Find the first non-null UI element in the array as reference
            MonoBehaviour firstValidElement = uiElements.FirstOrDefault(e => e != null);
            if (firstValidElement != null)
            {
                return GetPanelParent(firstValidElement); // Call single-element version to get parent
            }
        }
        return null;
    }

    /// <summary>
    /// Sets a GameObject and its children active or inactive.
    /// </summary>
    private void SetPanelActive(GameObject panel, bool isActive)
    {
        if (panel != null)
        {
            panel.SetActive(isActive);
        }
    }


    /// <summary>
    /// Sets up listeners and initial values for joint sliders.
    /// </summary>
    private void SetupJointSliders()
    {
        if (jointSliders == null || jointSliders.Length == 0) return;

        for (int i = 0; i < jointSliders.Length; i++)
        {
            int jointIndex = i; // Capture loop variable to ensure anonymous method uses correct index
            if (jointSliders[jointIndex] != null)
            {
                jointSliders[jointIndex].onValueChanged.AddListener(value => OnJointSliderChanged(jointIndex, value));

                // Initialize current joint angles array
                _currentJointAngles[jointIndex] = jointSliders[jointIndex].value;

                // Update display text
                if (jointAngleTexts != null && jointAngleTexts.Length > jointIndex && jointAngleTexts[jointIndex] != null)
                {
                    jointAngleTexts[jointIndex].text = jointSliders[jointIndex].value.ToString("F1") + "°";
                }
            }
        }
    }

    /// <summary>
    /// Called when a joint slider's value changes, publishes a JointSpaceTeaching intent.
    /// Only responds to slider changes in Joint Teaching mode.
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
            // Publish intent in real-time.
            OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateJointSpaceTeachingIntent(_currentJointAngles));
        }
    }

    /// <summary>
    /// Sets up listeners for Task Control UI elements.
    /// </summary>
    private void SetupTaskControlUI()
    {
        // Bind execute button event
        if (executeTaskButton != null)
        {
            executeTaskButton.onClick.AddListener(OnExecuteTaskButtonClicked);
        }
        // Note: Input fields for target pose don't need direct listeners here
        // as their values are read when executeTaskButton is clicked.
    }

    /// <summary>
    /// Sets up listeners and initial values for gripper control UI.
    /// </summary>
    private void SetupGripperControlUI()
    {
        if (toggleGripperButton != null)
        {
            toggleGripperButton.onClick.AddListener(OnToggleGripperButtonClicked);
        }
        // Initialize gripper status display
        UpdateGripperStatusText();
    }

    /// <summary>
    /// Called when the unified gripper control button is clicked.
    /// </summary>
    private void OnToggleGripperButtonClicked()
    {
        _isGripperOpen = !_isGripperOpen; // Toggle gripper state

        GripperState desiredState = _isGripperOpen ? GripperState.Open : GripperState.Close;

        // Publish an independent gripper control intent
        OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateGripperControlIntent(desiredState));
        Debug.Log($"RobotInputManager: Publishing independent gripper control intent: {(desiredState == GripperState.Open ? "Open" : "Close")}");

        // Update UI text display
        UpdateGripperStatusText();
    }

    /// <summary>
    /// Updates the gripper status text display.
    /// </summary>
    private void UpdateGripperStatusText()
    {
        if (gripperStatusText != null)
        {
            gripperStatusText.text = _isGripperOpen ? "Open" : "Close";
        }
    }

    /// <summary>
    /// Called when the execute task button is clicked, publishes a TaskControl intent.
    /// </summary>
    private void OnExecuteTaskButtonClicked()
    {
        if (_currentSelectedMode == ControlMode.TaskControl)
        {
            // Collect target position and Euler angles
            Vector3 targetPos = new Vector3(
                ParseInput(targetPosXInput),
                ParseInput(targetPosYInput),
                ParseInput(targetPosZInput)
            );
            Vector3 targetEuler = new Vector3(
                ParseInput(targetPitchInput), // X-axis corresponds to Pitch
                0f,                            // Y-axis corresponds to Yaw, fixed at 0 for 5-DOF arms
                ParseInput(targetRollInput)    // Z-axis corresponds to Roll
            );

            // Publish TaskControl intent
            OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateTaskControlIntent(targetPos, targetEuler));
            Debug.Log($"RobotInputManager: Publishing TaskControl intent. Target Position: {targetPos}, Euler Angles: {targetEuler}");
        }
        else
        {
            Debug.LogWarning("RobotInputManager: Not in Task Control mode, cannot execute task.", this);
        }
    }

    /// <summary>
    /// Parses the text of an InputField into a float.
    /// </summary>
    private float ParseInput(TMP_InputField inputField)
    {
        if (inputField != null && float.TryParse(inputField.text, out float result))
        {
            return result;
        }
        // If InputField is null or parsing fails, return 0 and issue a warning (decide whether to warn based on needs)
        if (inputField == null) Debug.LogWarning("RobotInputManager: Input field reference is null, returning 0.", this);
        else Debug.LogWarning($"RobotInputManager: Could not parse text '{inputField.text}' from input field '{inputField.name}' to float, returning 0.", this);
        return 0f;
    }


    /// <summary>
    /// Sets up listeners for the independent automation buttons (Reset and Place).
    /// </summary>
    private void SetupAutomationButtons() // Renamed from SetupPlayModeUI
    {
        // Bind Reset Button event
        if (resetButton != null)
        {
            resetButton.onClick.AddListener(OnResetButtonClicked);
        }

        // Bind Place Button event
        if (placeButton != null)
        {
            placeButton.onClick.AddListener(OnPlaceButtonClicked);
        }
    }

    /// <summary>
    /// Called when the "Reset" button is clicked. Publishes a request for the robot to go to its home position.
    /// </summary>
    private void OnResetButtonClicked()
    {
        // Now, this button directly triggers an event that MotionPlanner can listen to.
        _currentEndEffectorPosition = homePosition; // Reset position to home
        _currentEndEffectorEulerAngles = homeEulerAngles; // Reset orientation to home
        OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateTaskControlIntent(homePosition, homeEulerAngles));
        Debug.Log("RobotInputManager: 'Reset' button clicked. Requesting robot to return home.");
    }

    /// <summary>
    /// Called when the "Place" button is clicked. Publishes a request for the robot to go to the place position.
    /// </summary>
    private void OnPlaceButtonClicked()
    {
        // This button now directly triggers an event that MotionPlanner can listen to.
        OnRobotControlIntentUpdated?.Invoke(RobotControlIntent.CreateTaskControlIntent(placePosition, placeEulerAngles));
        Debug.Log("RobotInputManager: 'Place' button clicked. Requesting robot to move to place position.");
    }
}