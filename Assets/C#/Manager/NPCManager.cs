using System.Collections.Generic;
using UnityEngine;

public class NPCManager : MonoBehaviour
{

    public static NPCManager Instance;


    private Dictionary<NPCData, NPCRuntime> npcMap
        = new Dictionary<NPCData, NPCRuntime>();

    public List<NPCRuntime> GetAllNPC()
    {
        return new List<NPCRuntime>(npcMap.Values);
    }
    private void Awake()
    {
       
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitNPC();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 初始化NPC
    /// </summary>
    public void InitNPC()
    {
       
        NPCData[] npcs = Resources.LoadAll<NPCData>("NPC");

        

        foreach (var npc in npcs)
        {
            NPCRuntime runtime = new NPCRuntime(npc);

            npcMap.Add(npc, runtime);
           
        }


        Debug.Log("NPC运行时初始化完成");
    }

    /// <summary>
    /// 设置NPC状态
    /// </summary>
    public void SetState(NPCData npc, NPCState state, int days = 0)
    {
        NPCRuntime runtime = GetRuntime(npc);

        if (runtime == null)
        {
            Debug.LogError("找不到NPC运行数据：" + npc.npcName);
            return;
        }


        runtime.SetState(state, days);
    }
    //开始任务
    public void StartMission(NPCData npc, Mission mission)
    {
        NPCRuntime runtime = GetRuntime(npc);


        if (runtime == null)
            return;


        runtime.State = NPCState.Busy;

        runtime.CurrentMission = mission;
    }


    public void Injured(NPCRuntime npc, int days)
    {
        SetState(npc.Data, NPCState.Injured, days);
    }

    //恢复空闲状态
    public void Recover(NPCRuntime npc)
    {
        NPCRuntime runtime = GetRuntime(npc.Data);


        if (runtime == null)
            return;


        runtime.State = NPCState.Idle;

        runtime.StateRemainDays = 0;

        runtime.CurrentMission = null;
    }
    /// <summary>
    /// 获取NPC运行数据
    /// </summary>
    public NPCRuntime GetRuntime(NPCData npc)
    {
        if (npcMap.ContainsKey(npc))
        {
            return npcMap[npc];
        }


        return null;
    }



    /// <summary>
    /// 每天推进
    /// </summary>
    public void OnDayPassed()
    {
        foreach (var npc in npcMap.Values)
        {
            npc.OnDayPassed();
        }
    }


}