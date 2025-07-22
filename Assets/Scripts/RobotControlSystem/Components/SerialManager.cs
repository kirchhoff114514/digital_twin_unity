using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using UnityEngine;
//串口连接、信号首发管理类
public class SerialManager : Single<SerialManager>
{
    private SerialPort serialPort;  //初始化一个端口对象

    public SerialManager()
    {//构造
        
    }

    #region 一. 扫描可用端口
    public string[] ScanPort()
    {
        List<string> portsTemp = new List<string>();
        bool mark = false;
        for (int i = 0; i < 10; i++)
        {
            try
            {
                SerialPort portTest=new SerialPort("COM"+(i+1).ToString());
                portTest.Open();
                portTest.Close();  //测试一下能否打开
                portsTemp.Add("COM"+(i+1).ToString()); //将可用的串口添加到列表
                mark = true;
            }
            catch (Exception e)
            {
                continue;//如果发生异常，说明该串口不存在或不可用，继续下一个串口的测试
            }
        }

        if (mark)
        {
            string[] ports = portsTemp.ToArray(); //列表转换成数组并返回
            return ports;
        }
        else
        {
            Debug.LogError("没有找到可用的端口！");
            return null;
        }
    }
    #endregion

    #region 二. 连接串口
    public void ConnectSerialPort(string portName, int baudRate)
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.Open();
            serialPort.Encoding=Encoding.UTF8;//设置编码格式，方便显示中文
            //serialPort.DataReceived += RecieveData; //
            serialPort.Write("$INIT#");//发送初始化指令，用于初始化串口
            Debug.Log($"串口{portName}已打开");
        }
        catch (Exception e)
        {
            serialPort = new SerialPort();
            Debug.LogError("打开串口失败：" + e.Message);
            throw;
        }
    }
    #endregion

    #region 三. 关闭串口

    public void CloseSerialPort()
    {
        if(serialPort.IsOpen && serialPort !=null)
            serialPort.Close();
        Debug.Log("串口已关闭");
    }
    
    #endregion

    #region 四.(协程或非协程)向串口发送String数据，结构为："${Servo/Stepper}:{ID}:{angle}#"
    //发送数据的格式：
    //报头：$
    //第一个数据位：0：表示舵机；1：表示电机
    //第二个数据位：舵机或者电机的ID
    //数据：要发送的角度
    //报尾：#
    public void SendData(int Type,int ID,int angle)
    {//非协程的方法
        string sendCommand = "$"+Type+":"+ID+":"+angle+"#";
        try
        {
            serialPort.WriteLine(sendCommand);
            Debug.Log($"已发送: {sendCommand}");
        }
        catch (Exception e)
        {
            Debug.LogError($"发送数据时出错"+e.Message);
            throw;
        }
    }

    public IEnumerator SendData(int Type, int ID, int angle, float delayTime)
    {//协程的方法，用于联动时逐条发送数据
        yield return new WaitForSeconds(delayTime);
        string sendCommand = "$"+Type+":"+ID+":"+angle+"#";
        try
        {
            serialPort.WriteLine(sendCommand);
            Debug.Log($"已发送: {sendCommand}");
        }
        catch (Exception e)
        {
            Debug.LogError($"发送数据时出错"+e.Message);
            throw;
        }
    }
    #endregion

    #region 五.使用协程从下位机接收数据，结构为："${Servo/Stepper}:{ID}:{angle}#"
    public IEnumerator WaitForRecieveData(int delayTime,DataCallback callback)
    {//定义协程，并且传递一个回调函数，用于在协程中回调传递数据
        yield return new WaitForSeconds(delayTime); //等待，需要测试大约舵机执行完成的时间
        //serialPort.DiscardInBuffer(); // 清空接收缓存
        if (serialPort.IsOpen && serialPort.BytesToRead > 0)
        {
            try
            {
                string receiveCommand = serialPort.ReadLine(); //只接收一行数据
                //string receiveCommand = serialPort.ReadExisting(); //接收所有的数据，有可能有空行
                Debug.Log("接收到数据：" + receiveCommand);
                //处理接收到的数据
                ReceivedData resultData=ProcessData(receiveCommand);
                callback(resultData);  //调用回调函数，传递数据
            }
            catch(SystemException e)
            {
                Debug.LogError($"读取串口数据失败：{e.Message}");
            }
        }
    }
    //简单的接收数据方法
    public String RecieveData()
    {//此方法只限于低频率使用场景，有缓冲区挤压的风险
        //比较合适的方法是使用线程从串口接收数据，然后将数据存储在一个队列中
        if (serialPort.BytesToRead > 0)
        {
            //string receiveCommand = serialPort.ReadLine();
            string receiveCommand = serialPort.ReadExisting();
            return receiveCommand;
        }
        return null;
    }
    #endregion
    
    #region 六. 接收数据后处理成结构体
    // 定义回调委托，在协程中回调传递数据
    public delegate void DataCallback(ReceivedData result);
    public ReceivedData ProcessData(string data)
    {
        ReceivedData _recieveData=new ReceivedData(); //用于存放解析后的数据
        //检查字符串的有效性：报头报尾
        //Debug.Log("data数据错哪里了"+data);
        if(string.IsNullOrEmpty(data)||!data.StartsWith("$")||!data.EndsWith("#"))
            Debug.LogError("无效的数据格式"+data);
        try
        {
            string middleData=data.Substring(1, data.Length - 2); //去掉报头和报尾
            string[] intData = middleData.Split(':'); //按照冒号分割数据
            if(intData.Length!=3)
                Debug.LogError("无效的数据格式，实际分割成了"+intData.Length+"段数据");
            else
            {
                _recieveData.motorType = int.Parse(intData[0]);
                _recieveData.motorID = int.Parse(intData[1]);
                _recieveData.motorAngle = int.Parse(intData[2]);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("解析数据时出错: " + e.Message);
            throw;
        }
        return _recieveData;
    }
    #endregion

    #region 七.发送舵机初始化命令

    public void SendZeroCommand()
    {
        string initCommand = "$INIT#"; //约定初始化的字符串格式
        serialPort.Write(initCommand);
        //Debug.Log("初始化命令已发送"+initCommand);
    }

    #endregion

    #region 八.清空缓冲区，关闭并重新打开串口

    public void resetSystem()
    {
        if (serialPort.IsOpen && serialPort != null)
        {
            try
            {
                serialPort.DiscardInBuffer(); // 清空接收缓冲区
                serialPort.DiscardOutBuffer();// 清空发送缓冲区
                //serialPort.Close();
                //serialPort.Open();
            }
            catch (Exception e)
            {
                Console.WriteLine("串口重新连接失败"+e.Message);
                throw;
            }
        }
    }
    
    #endregion
}

#region 结构体：用于存放串口传输的数据
public struct ReceivedData
{
    public int motorType;
    public int motorID;
    public int motorAngle;
}
#endregion

