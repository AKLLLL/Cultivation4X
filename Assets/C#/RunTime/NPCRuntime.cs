using UnityEngine;

public class NPCRuntime
{
    // 对应静态数据
    public NPCData Data;
    //-----------------
    //成长数据
    //-----------------

    public int Level;

    public int Exp;

    public int Gold;
    // 当前状态
    public NPCState State;

    // 状态剩余时间
    public int StateRemainDays;
    // 当前任务
    public Mission CurrentMission;
    public NPCRuntime(NPCData data)
    {
        Data = data;
        //读取初始值
        Level = data.level;
        Exp = data.exp;
        Gold = 0;

        State = NPCState.Idle;

        StateRemainDays = 0;
    }
    public int Attack
    {
        get
        {
            return Data.attack;
        }
    }
    /// <summary>
    /// 设置状态
    /// </summary>
    public void SetState(NPCState state, int days = 0)
    {
        Debug.Log(
        $"{Data.npcName} 状态变化: {State} -> {state}"
    );
        State = state;
        StateRemainDays = days;
    }
    /// <summary>
    /// 每日推进
    /// </summary>
    public void OnDayPassed()
    {
        Debug.Log(
       $"{Data.npcName} 当前状态:{State} 剩余:{StateRemainDays}"
   );
        if (StateRemainDays > 0)
        {
            StateRemainDays--;

            if (StateRemainDays == 0)
            {
                State = NPCState.Idle;
                CurrentMission = null;
            }
        }
    }


    public bool CanDispatch()
    {
        return State == NPCState.Idle;
    }
}