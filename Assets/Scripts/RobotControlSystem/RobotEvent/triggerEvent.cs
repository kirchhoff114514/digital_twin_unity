using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class triggerEvent : MonoBehaviour
{
    public RobotControlSystemManager manager; // 引用 RobotArmExecutor 脚本
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void OnTriggerEnter(Collider other)
    {

        if(other.CompareTag("goods") &&manager.robotArmExecutor._currentGripperState == GripperState.Close)
        {
            other.gameObject.transform.SetParent(transform.parent);
            other.gameObject.GetComponent<Rigidbody>().isKinematic = true; // 停止物理模拟
            Debug.Log("Goods is in the gripper");
        }

    }
    private void OnTriggerExit(Collider other){
        if(other.CompareTag( "goods")&& manager.robotArmExecutor._currentGripperState == GripperState.Open){
            other.gameObject.GetComponent<Rigidbody>().isKinematic = false; // 恢复物理模拟 
            other.gameObject.GetComponent<Rigidbody>().useGravity = true; // 恢复物理模拟 
            other.gameObject.transform.SetParent(null); // 取消父对
        }
    }
}
