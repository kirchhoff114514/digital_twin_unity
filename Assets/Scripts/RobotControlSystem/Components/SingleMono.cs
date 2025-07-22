using UnityEngine;

public class SingleMono<T> : MonoBehaviour where T : SingleMono<T>
{
    private static T instance;
    private static GameObject go;

    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                if (!go)
                {
                    //如果场内没有SingleMono节点，就建立一个
                    go = GameObject.Find("SingleMono");
                    if (!go)
                        go = new GameObject("SingleMono");
                }

                DontDestroyOnLoad(go);
                instance = go.GetComponent<T>(); //如果go上没有挂脚本
                if (!instance)
                {
                    instance = go.AddComponent<T>();
                }
            }

            return instance;
        }
    }
}
