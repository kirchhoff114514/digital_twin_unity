// File: Assets/Scripts/RobotControlSystem/Components/SerialCommunicator.cs
// Description: 处理与下位机的串口双向通信。发送目标关节角度和夹爪角度，接收实际关节角度和夹爪状态。
//             支持串口自动检测和断线重连。

using UnityEngine;
using System;
using System.IO.Ports; // 串口通信命名空间
using System.Threading; // 多线程支持
using System.Collections.Generic; // 用于线程间数据队列
using System.Text.RegularExpressions; // 用于解析接收到的字符串

// 引入 GripperState 枚举定义所在的命名空间，或者确保它在全局可访问
// using YourNamespace.Core; // 假设 GripperState 在 RobotControlIntent.cs 中定义

/// <summary>
/// SerialCommunicator 负责管理与下位机的串口双向通信。
/// 它发送期望的目标关节角度和夹爪角度，并接收下位机反馈的实际关节角度和夹爪状态。
/// 支持串口的自动检测和断线重连。
/// </summary>
public class SerialCommunicator : MonoBehaviour
{
    // --- 事件定义 ---
    /// <summary>
    /// 当接收到下位机反馈的实际状态时触发此事件。
    /// MotionPlanner 和 RobotArmExecutor 将订阅此事件。
    /// </summary>
    public event Action<float[], GripperState> OnActualRobotStateReceived; // 关节角度[], 夹爪状态

    [Header("串口设置")]
    [Tooltip("期望的波特率，应与下位机设置一致。")]
    public int baudRate = 115200;

    // 移除了 portNameKeyword，因为我们将遍历所有端口
    // public string portNameKeyword = "Silicon"; // 可根据你的ESP32串口驱动调整

    [Tooltip("尝试重新连接串口的时间间隔（秒）。")]
    public float reconnectInterval = 5.0f;

    [Tooltip("用于在遍历串口时识别目标设备的握手命令。")]
    public string handshakeCommand = "IDENTIFY?"; // 下位机需要能响应此命令
    
    [Tooltip("目标设备响应握手命令的预期字符串。")]
    public string expectedHandshakeResponse = "ROBOT_READY"; // 下位机预期会发送的响应

    [Header("通信协议参数")]
    [Tooltip("机械臂的自由度 (DOF)。这里指关节数量，不含夹爪。")]
    public int robotDOF = 5; // 关节数量
    [Tooltip("夹爪的数量。这里指夹爪的驱动轴数量。")]
    public int gripperDOF = 1; // 夹爪数量 (步进电机)

    [Tooltip("接收数据包的超时时间（毫秒）。如果在此时间内未收到完整包，则清空缓冲区。")]
    public int receiveTimeoutMs = 200; // 根据实际通信频率调整

    // **保留夹爪状态转换阈值，因为 SerialCommunicator 仍然负责将接收到的原始角度转换为 GripperState**
    [Header("夹爪状态转换（用于接收）")]
    [Tooltip("将接收到的夹爪电机角度转换为 'Close' 状态的上限阈值。例如，0-30度认为是关闭。")]
    public float gripperCloseThreshold = 30.0f;
    [Tooltip("将接收到的夹爪电机角度转换为 'Open' 状态的下限阈值。例如，60-90度认为是打开。")]
    public float gripperOpenThreshold = 60.0f;


    // --- 内部状态 ---
    private SerialPort _serialPort;
    private bool _isConnected = false;
    private float _reconnectTimer = 0f;
    private string _currentPortName = "";

    // 线程安全队列，用于从读取线程向主线程传递数据
    private Queue<Tuple<float[], GripperState>> _receivedDataQueue = new Queue<Tuple<float[], GripperState>>();
    private Thread _readThread;
    private bool _keepReading = false;

    // 接收缓冲区和相关状态
    private string _rxBuffer = "";
    private long _lastReceiveTimeMs = 0;

    // 正则表达式用于解析接收到的数据包
    // 假设接收格式为：ACTUAL:J1:XX.X;J2:XX.X;...J6:XX.X;CRC:YY#
    private Regex _actualDataRegex;


    // --- Unity 生命周期方法 ---
    void Awake()
    {
        // 构建正则表达式，以匹配接收到的实际角度数据格式
        string jointPattern = "";
        for (int i = 0; i < robotDOF; i++)
        {
            jointPattern += $"J{i + 1}:([+-]?\\d+\\.\\d{{1,}});";
        }
        // Gripper (J6) 仍然以角度形式接收，因为这是下位机通常发送的原始数据
        jointPattern += $"J{robotDOF + gripperDOF}:([+-]?\\d+\\.\\d{{1,}});"; 

        _actualDataRegex = new Regex($"^ACTUAL:{jointPattern}CRC:([0-9A-Fa-f]{{2}})$");

        Debug.Log("SerialCommunicator: 初始化完成。");
    }

    void Start()
    {
        TryConnectSerialPort();
    }

    void Update()
    {
        // 自动重连逻辑
        if (!_isConnected)
        {
            _reconnectTimer += Time.deltaTime;
            if (_reconnectTimer >= reconnectInterval)
            {
                _reconnectTimer = 0f;
                Debug.Log("SerialCommunicator: 尝试重新连接串口...");
                TryConnectSerialPort();
            }
        }

        // 处理来自读取线程的数据
        ProcessReceivedDataQueue();
    }

    void OnApplicationQuit()
    {
        DisconnectSerialPort();
    }

    void OnDestroy()
    {
        DisconnectSerialPort();
    }

    // --- 串口连接与断开 ---

    /// <summary>
    /// 尝试连接到一个可用的串口。
    /// 遍历所有可用串口，并尝试发送握手命令以识别目标设备。
    /// </summary>
    private void TryConnectSerialPort()
    {
        if (_isConnected) return;

        string[] ports = SerialPort.GetPortNames();
        Debug.Log($"SerialCommunicator: 发现 {ports.Length} 个可用串口。");

        foreach (string port in ports)
        {
            Debug.Log($"SerialCommunicator: 尝试连接到串口: {port}...");
            SerialPort tempSerialPort = null;
            
            
            try
            {
                tempSerialPort = new SerialPort(port, baudRate);
                tempSerialPort.ReadTimeout = 500; // 临时设置一个较长的读取超时，以便等待握手响应
                tempSerialPort.WriteTimeout = 500; // 写入超时
                tempSerialPort.Open();

                // 清空缓冲区，避免旧数据干扰
                tempSerialPort.DiscardInBuffer();
                tempSerialPort.DiscardOutBuffer();

                // 发送握手命令
                string command = handshakeCommand + "$"; // 假设命令也以 # 结尾
                tempSerialPort.Write(command);
                Debug.Log($"SerialCommunicator: 发送握手命令 '{command.TrimEnd('#')}' 到 {port}");

                // 等待并读取响应
                // 这里我们不是在一个单独的读取线程中，所以需要同步读取
                // 简单起见，我们读取一行或者等待一段时间
                string response = tempSerialPort.ReadLine(); // 假设响应以换行符结束
                
                // 移除可能的包结束符，这里假设是 # 或者 \n
                string trimmedResponse = response.TrimEnd('#', '\n', '\r'); 
                Debug.Log($"SerialCommunicator: 从 {port} 收到响应: '{trimmedResponse}'");

                if (trimmedResponse.Contains(expectedHandshakeResponse))
                {
                    Debug.Log($"SerialCommunicator: 成功识别目标设备在串口: {port}");
                    _currentPortName = port;
                    _serialPort = tempSerialPort; // 赋值给实际使用的串口对象
                    _isConnected = true;
                    _reconnectTimer = 0f; // 重置重连计时器

                    // 启动读取线程
                    _keepReading = true;
                    _readThread = new Thread(ReadSerialData);
                    _readThread.IsBackground = true; // 设置为后台线程，随主程序退出
                    _readThread.Start();
                    
                    // 重设为实际的接收超时
                    _serialPort.ReadTimeout = receiveTimeoutMs; 

                    Debug.Log($"SerialCommunicator: 成功连接到串口: {_currentPortName} @ {baudRate} Baud");
                    return; // 找到并连接成功，退出循环
                }
                else
                {
                    Debug.LogWarning($"SerialCommunicator: 串口 {port} 响应不匹配，收到: '{trimmedResponse}'，期望包含: '{expectedHandshakeResponse}'");
                }
            }
            catch (TimeoutException)
            {
                Debug.LogWarning($"SerialCommunicator: 连接到串口 {port} 时握手超时，可能不是目标设备。");
            }
            catch (Exception e)
            {
                Debug.LogError($"SerialCommunicator: 连接或识别串口 {port} 失败: {e.Message}", this);
            }
            finally
            {
                // 确保关闭并释放未被选中的串口
                if (tempSerialPort != null && tempSerialPort.IsOpen)
                {
                    tempSerialPort.Close();
                    tempSerialPort.Dispose();
                }
            }
        }

        // 如果遍历完所有串口都未能成功连接
        if (!_isConnected)
        {
            Debug.LogWarning("SerialCommunicator: 未能找到并连接到目标串口。");
        }
    }

    /// <summary>
    /// 断开串口连接。
    /// </summary>
    private void DisconnectSerialPort()
    {
        if (_serialPort != null && _serialPort.IsOpen)
        {
            _keepReading = false; // 停止读取线程
            if (_readThread != null && _readThread.IsAlive)
            {
                // 等待线程结束，最多200ms
                _readThread.Join(200);
                if (_readThread.IsAlive) // 如果线程仍然存活，强制终止（不推荐，但有时必要）
                {
                    _readThread.Interrupt();
                }
            }
            _serialPort.Close();
            _serialPort.Dispose();
            _serialPort = null;
            _isConnected = false;
            Debug.Log($"SerialCommunicator: 串口 {_currentPortName} 已关闭。", this);
        }
    }

    // --- 数据发送 ---

    /// <summary>
    /// 发送目标关节角度和夹爪角度给下位机。
    /// 数据格式: J1:XX.X;J2:XX.X;...J5:XX.X;J6:XX.X;CRC:YY#
    /// J6 是期望发送给夹爪的电机角度。
    /// </summary>
    /// <param name="jointAngles">5个目标关节角度。</param>
    /// <param name="desiredGripperAngle">期望发送给夹爪的电机角度。</param>
    public void SendDesiredAnglesToHardware(float[] jointAngles, float desiredGripperAngle)
    {
        if (!_isConnected || _serialPort == null || !_serialPort.IsOpen)
        {
            // Debug.LogWarning("SerialCommunicator: 串口未连接，无法发送数据。", this);
            return;
        }

        if (jointAngles == null || jointAngles.Length != robotDOF)
        {
            Debug.LogError($"SerialCommunicator: 目标关节角度数组无效。期望 {robotDOF} 个关节，收到 {jointAngles?.Length ?? 0}。", this);
            return;
        }

        // 格式化数据字符串
        string dataPart = "";
        for (int i = 0; i < robotDOF; i++)
        {
            dataPart += $"J{i + 1}:{jointAngles[i]:F1};"; // J1到J5
        }
        dataPart += $"J{robotDOF + 1}:{desiredGripperAngle:F1};"; // J6为夹爪目标角度

        // 计算 CRC8
        byte crc = CalcCRC8(dataPart);
        string packetToSend = $"{dataPart}CRC:{crc:X2}#"; // :X2 将字节格式化为两位十六进制，并以 # 结束

        try
        {
            _serialPort.Write(packetToSend);
            // Debug.Log($"SerialCommunicator: 发送 --> {packetToSend.TrimEnd('#')}"); // 调试时打印
        }
        catch (Exception e)
        {
            Debug.LogError($"SerialCommunicator: 发送数据失败: {e.Message}。串口连接可能已中断。", this);
            _isConnected = false; // 标记为断开，以便重连
        }
    }

    // --- 数据接收 (通过单独线程) ---

    /// <summary>
    /// 在单独的线程中读取串口数据。
    /// 此方法会持续尝试从串口读取数据，并解析完整的数据包。
    /// </summary>
    private void ReadSerialData()
    {
        Debug.Log("SerialCommunicator: 读取线程已启动。");
        _rxBuffer = ""; // 清空缓冲区
        _lastReceiveTimeMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(); // 记录初始时间

        while (_keepReading)
        {
            try
            {
                // 读取所有可用字节
                while (_serialPort.BytesToRead > 0)
                {
                    char c = (char)_serialPort.ReadByte();
                    _rxBuffer += c;
                    _lastReceiveTimeMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(); // 更新接收时间
                    // Debug.Log($"Received char: {c}, Buffer: {_rxBuffer}"); // 调试每字节接收

                    // 检查是否收到包结束符 '#'
                    if (c == '#')
                    {
                        ProcessReceivedPacket(_rxBuffer);
                        _rxBuffer = ""; // 处理完后清空缓冲区
                    }
                }

                // 处理超时：如果长时间未收到数据，清空缓冲区以避免脏数据
                if (!string.IsNullOrEmpty(_rxBuffer) && (DateTimeOffset.Now.ToUnixTimeMilliseconds() - _lastReceiveTimeMs > receiveTimeoutMs))
                {
                    Debug.LogWarning($"SerialCommunicator: 接收超时，清空缓冲区: {_rxBuffer.Length} 字节。");
                    _rxBuffer = "";
                }

                Thread.Sleep(1); // 避免CPU占用过高
            }
            catch (TimeoutException)
            {
                // 读取超时，正常情况，继续循环
            }
            catch (ThreadInterruptedException)
            {
                // 线程被中断，准备退出
                Debug.Log("SerialCommunicator: 读取线程被中断。");
                break;
            }
            catch (Exception e)
            {
                Debug.LogError($"SerialCommunicator: 读取线程发生错误: {e.Message}");
                _isConnected = false; // 标记为断开，主线程会尝试重连
                break; // 退出读取循环
            }
        }
        Debug.Log("SerialCommunicator: 读取线程已停止。");
    }

    /// <summary>
    /// 处理一个完整的接收数据包。
    /// </summary>
    /// <param name="packet">完整的接收字符串，包含CRC和结束符。</param>
    private void ProcessReceivedPacket(string packet)
    {
        // 移除结束符 '#' 和可能的换行符
        string trimmedPacket = packet.TrimEnd('#', '\n', '\r');

        // Debug.Log($"SerialCommunicator: 收到完整包 --> {trimmedPacket}"); // 调试时打印

        Match match = _actualDataRegex.Match(trimmedPacket);
        if (!match.Success)
        {
            Debug.LogWarning($"SerialCommunicator: 接收包格式不匹配或不完整: '{trimmedPacket}'");
            return;
        }

        float[] actualJointAngles = new float[robotDOF];
        float rawGripperAngle = 0f; // 接收到的原始夹爪电机角度

        // 提取数据部分和 CRC 字符串
        string dataPartForCrc = "ACTUAL:";
        for (int i = 0; i < robotDOF; i++)
        {
            if (float.TryParse(match.Groups[i + 1].Value, out float angle))
            {
                actualJointAngles[i] = angle;
                // 重新构建，确保和发送端格式一致，用于CRC校验
                dataPartForCrc += $"J{i + 1}:{actualJointAngles[i]:F1};"; 
            }
            else
            {
                Debug.LogError($"SerialCommunicator: 解析关节 J{i + 1} 角度失败: {match.Groups[i + 1].Value}");
                return;
            }
        }

        // 提取夹爪角度 (J6)
        if (float.TryParse(match.Groups[robotDOF + 1].Value, out float gripperA))
        {
            rawGripperAngle = gripperA;
            dataPartForCrc += $"J{robotDOF + 1}:{rawGripperAngle:F1};"; // 重新构建 J6
        }
        else
        {
            Debug.LogError($"SerialCommunicator: 解析夹爪 J{robotDOF + 1} 角度失败: {match.Groups[robotDOF + 1].Value}");
            return;
        }

        // 移除末尾分号以便CRC计算
        dataPartForCrc = dataPartForCrc.TrimEnd(';'); 

        string receivedCrcStr = match.Groups[robotDOF + gripperDOF + 1].Value; // CRC是最后一个捕获组

        // 校验 CRC
        byte expectedCrc = CalcCRC8(dataPartForCrc);
        byte receivedCrc;
        try
        {
            receivedCrc = Convert.ToByte(receivedCrcStr, 16);
        }
        catch (FormatException)
        {
            Debug.LogError($"SerialCommunicator: CRC字符串 '{receivedCrcStr}' 格式无效。");
            return;
        }

        if (expectedCrc != receivedCrc)
        {
            Debug.LogWarning($"SerialCommunicator: CRC校验失败！期望: {expectedCrc:X2}, 收到: {receivedCrc:X2}。原始包: '{trimmedPacket}'");
            return;
        }

        // 将接收到的原始夹爪角度转换为 GripperState
        GripperState actualGripperState = ConvertAngleToGripperState(rawGripperAngle);

        // CRC 校验通过，将数据加入队列，等待主线程处理
        lock (_receivedDataQueue)
        {
            _receivedDataQueue.Enqueue(Tuple.Create(actualJointAngles, actualGripperState));
            // Debug.Log($"SerialCommunicator: 数据包解析成功并加入队列。J1:{actualJointAngles[0]:F1}, G:{actualGripperState}");
        }
    }

    /// <summary>
    /// 在Unity主线程中处理从读取线程传递过来的数据。
    /// </summary>
    private void ProcessReceivedDataQueue()
    {
        lock (_receivedDataQueue)
        {
            while (_receivedDataQueue.Count > 0)
            {
                Tuple<float[], GripperState> data = _receivedDataQueue.Dequeue();
                OnActualRobotStateReceived?.Invoke(data.Item1, data.Item2);
            }
        }
    }

    /// <summary>
    /// 将接收到的夹爪电机角度转换为 GripperState。
    /// 此方法基于预设的阈值进行判断。
    /// </summary>
    /// <param name="motorAngle">实际接收到的夹爪电机角度。</param>
    /// <returns>对应的 GripperState (Open, Close, None)。</returns>
    private GripperState ConvertAngleToGripperState(float motorAngle)
    {
        // 假设 0度是完全关闭，90度是完全打开。
        // 使用阈值来容忍一些电机角度的误差或中间状态。
        if (motorAngle <= gripperCloseThreshold)
        {
            return GripperState.Close;
        }
        else if (motorAngle >= gripperOpenThreshold)
        {
            return GripperState.Open;
        }
        else
        {
            // 如果不在打开或关闭的明确范围内，标记为 None
            return GripperState.None; 
        }
    }

    // --- CRC8 计算辅助函数 ---

    // CRC8-Maxim (poly 0x31, init 0x00)
    private byte CalcCRC8(string data)
    {
        byte crc = 0x00;
        foreach (char c in data)
        {
            crc ^= (byte)c;
            for (int j = 0; j < 8; ++j)
            {
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x31) : (byte)(crc << 1);
            }
        }
        return crc;
    }
}

// 假设 GripperState 枚举定义如下（如果不在本文件中，请确保在其他可访问的文件中定义）
public enum GripperState
{
    Open,
    Close,
    None // 夹爪既不完全打开也不完全关闭的中间状态
}