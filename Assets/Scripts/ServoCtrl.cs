using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ServoCtrl : MonoBehaviour
{
    private int currentVirtualAngle = 0; // 内部角度计数器，0到180度
    private readonly int step = 45;      // 每次旋转的度数

    private int direction = 1;           // 旋转方向：1为正向（0->180），-1为反向（180->0）

    void Start()
    {
        // 确保虚拟舵机初始位置正确，例如设置到0度。
        // 这通常是唯一一次直接设置GameObject的绝对旋转，之后只用增量Rotate。
        transform.rotation = Quaternion.Euler(transform.rotation.eulerAngles.x, transform.rotation.eulerAngles.y, 0f);
        currentVirtualAngle = 0; // 确保内部计数器与初始视觉同步
        Debug.Log("虚拟舵机初始设置为 0°");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // 记录当前帧开始时，我们内部计数器认为的虚拟舵机位置
            int angleBeforeStep = currentVirtualAngle;

            // 计算下一个目标角度
            int nextTargetAngle = currentVirtualAngle + (step * direction);

            // --- 关键逻辑：处理边界并更新下一次的目标角度 ---
            if (direction == 1) // 当前是正向（0 -> 180）
            {
                if (nextTargetAngle > 180)
                {
                    // 如果下一步会超过180，则将目标设为180，并反转方向
                    currentVirtualAngle = 180;
                    direction = -1; // 为下一次点击准备反向
                }
                else
                {
                    // 否则，正常前进到下一个步进角度
                    currentVirtualAngle = nextTargetAngle;
                }
            }
            else // 当前是反向（180 -> 0）
            {
                if (nextTargetAngle < 0)
                {
                    // 如果下一步会低于0，则将目标设为0，并反转方向
                    currentVirtualAngle = 0;
                    direction = 1; // 为下一次点击准备正向
                }
                else
                {
                    // 否则，正常后退到下一个步进角度
                    currentVirtualAngle = nextTargetAngle;
                }
            }

            // 计算需要进行的增量旋转量
            // 这是从 `angleBeforeStep` 旋转到 `currentVirtualAngle` 的量
            float rotationAmount = currentVirtualAngle - angleBeforeStep;

            // 如果计算出的旋转量为0，这意味着我们从边界值再次点击时，
            // 内部计数器已经指向了边界，但实际位置没有动，那么我们这次就让它动起来。
            // 这解决了在0和180处停顿的问题。
            if (rotationAmount == 0 && (angleBeforeStep == 0 || angleBeforeStep == 180))
            {
                // 如果在边界且计算的旋转量为0（意味着内部角度没变），
                // 那么我们强制让它以反方向的步长旋转。
                rotationAmount = -step * direction; // 注意这里的direction是更新后的
                                                    // 如果是180，dir是-1，-step*-1 = +step，不对
                                                    // 应该是-step*旧dir，但旧dir没存
                                                    // 最直接的方式是，如果当前在0/180，且算出来rotationAmount是0，
                                                    // 就根据当前方向强制旋转step。

                // 修正：更精确地处理边界跳跃后的增量
                if (angleBeforeStep == 180 && direction == -1) // 刚从180反向，下次目标是135
                {
                    rotationAmount = -step; // 需要减45
                }
                else if (angleBeforeStep == 0 && direction == 1) // 刚从0反向，下次目标是45
                {
                    rotationAmount = step; // 需要加45
                }
                // 此时，currentVirtualAngle本身已经被设置为边界值了，我们需要手动修正
                // 这一步是为了让旋转发生，同时更新currentVirtualAngle到正确的下一个值
                currentVirtualAngle = angleBeforeStep + (int)rotationAmount; // 修正内部计数器

            }


            // 应用旋转
            transform.Rotate(0, 0, rotationAmount, Space.Self);

            Debug.Log($"前一角度: {angleBeforeStep}°，目标角度: {currentVirtualAngle}°，旋转量: {rotationAmount}°。方向: {(direction == 1 ? "正向" : "反向")}");

            // --- 与真实舵机通信 (如果适用) ---
            // var serialManager = FindObjectOfType<SerialSenderManager>();
            // if (serialManager != null)
            // {
            //     serialManager.SendData("$" + currentVirtualAngle.ToString() + "#");
            // }
        }
    }
}