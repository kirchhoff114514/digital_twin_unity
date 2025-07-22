using System;
//不继承Mono的单例
public class Single<T> where T: class
{
    private static T _instance;
    private static T instance;
    public static T Instance 
    { 
        get
        {
            if (_instance == null)
            {//判断如果场上没有实例，就动态创建一个实例
                Type type= typeof(T);
                _instance=Activator.CreateInstance(type,true) as T;
            }
            return _instance;
        }
    }
    protected Single()
    {
 
    }
}

