using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class move : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float acceleration = 5f;       // 加速率
    [SerializeField] private float deceleration = 8f;       // 减速率
    [SerializeField] private float maxSpeed = 30f;          // 最大速度

    [Header("转向设置")]
    [SerializeField] private float pitchSpeed = 60f;        // 上下转向速度
    [SerializeField] private float yawSpeed = 60f;         // 左右转向速度
    [SerializeField] private float rollSpeed = 40f;        // 滚转速度(倾斜)
    [SerializeField] private float rotationSmoothing = 5f;  // 转向平滑度

    [Header("自动回正")]
    [SerializeField] private bool autoLevel = true;         // 自动回正
    [SerializeField] private float levelingSpeed = 2f;     // 回正速度

    private float currentSpeed = 0f;
    private Vector3 currentRotation;
    private Vector3 targetRotation;

    void Update()
    {
        HandleInput();
        ApplyMovement();
        ApplyRotation();
    }

    void HandleInput()
    {
        // 前进控制 (空格键)
        if (Input.GetKey(KeyCode.Space))
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed, acceleration * Time.deltaTime);
        }
        else
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, deceleration * Time.deltaTime);
        }

        // 上下转向 (W/S)
        float pitchInput = Input.GetAxis("Vertical");
        targetRotation.x = pitchInput * pitchSpeed;

        // 左右转向 (A/D)
        float yawInput = Input.GetAxis("Horizontal");
        targetRotation.y = yawInput * yawSpeed;

        // 自动滚转效果 (根据转向自动倾斜)
        if (Mathf.Abs(yawInput) > 0.1f)
        {
            targetRotation.z = -yawInput * rollSpeed;
        }
        else if (autoLevel)
        {
            // 自动回正滚转
            targetRotation.z = Mathf.Lerp(targetRotation.z, 0f, levelingSpeed * Time.deltaTime);
        }
    }

    void ApplyMovement()
    {
        // 向前移动 (基于物体自身坐标系)
        transform.Translate(Vector3.forward * currentSpeed * Time.deltaTime, Space.Self);
    }

    void ApplyRotation()
    {
        // 平滑转向
        currentRotation = Vector3.Lerp(currentRotation, targetRotation, rotationSmoothing * Time.deltaTime);
        
        // 应用旋转 (基于局部坐标系)
        transform.Rotate(currentRotation * Time.deltaTime, Space.Self);
    }

    // 可选：在Inspector中显示当前速度
    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(10, 10, 300, 30), $"当前速度: {currentSpeed:F1}", style);
    }
}