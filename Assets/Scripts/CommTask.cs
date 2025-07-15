    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using System.IO.Ports;
    using UnityEngine.UI;
    using TMPro;
    //教案版：
    //Serial连接和数据发送
    public class CommTask : MonoBehaviour
    {
        public GameObject virtualServo; // 虚拟舵机对象;
        private string portName="COM9";
        private int baudRate = 115200; // 波特率
        private SerialPort serialPort;
        
        private int angle = 0;  //舵机角度初始值
        private readonly int step = 45;//每一步旋转的度数（用于按Space按钮发送一个Step的）

        private int flag = 1; //符号变量，用于数值的正负变换

        void Start()
        {
            #region 打开串口
            serialPort = new SerialPort(portName, baudRate)
            {
                ReadTimeout = 1000,  // 读取超时(ms)
                WriteTimeout = 1000   // 写入超时
            };
            
            try {
                serialPort.Open();
                Debug.Log($"串口 {portName} 已打开");
            } catch (System.Exception e) {
                Debug.LogError($"打开串口失败: {e.Message}");
            }
            #endregion
            

        }
        
        void Update()
        {
            Debug.Log("update");
            #region 使用Space按键发送角度，让舵机从0转到180度再转回0
            if (Input.GetKeyDown(KeyCode.Space))
            {//按下Space按钮循环0-180-0的角度发送
                angle += step*flag;
                if (angle >= 180)
                {
                    flag = -1;
                    angle = 180;
                }
                else if(angle<=0)
                {
                    angle = 0;
                    flag = 1;
                }
                SendAngle(angle); //发送角度
            }
            #endregion
        
        }
        
        public void SendAngle(int angle)
        {//发送角度数据的方法
            if (serialPort.IsOpen) 
            {
                //string command = $"{angle}\n"; // 也可以写成这种方式
                string command = "$"+angle.ToString()+"#"; //加上报头和报尾
                try
                {//增加异常处理
                    serialPort.WriteLine(command);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"发送数据时出错"+e.Message);
                }
            }
        }

        void OnDestroy()
        {
            if (serialPort != null && serialPort.IsOpen) 
            {
                serialPort.Close(); // 退出时关闭串口
            }
        } 
    }
