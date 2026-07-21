using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// 任务管理器
///
/// 职责：
/// 1. 加载任务模板 MissionData
/// 2. 根据模板创建 Mission实例
/// 3. 管理正在运行的任务
/// 4. 推进任务时间
/// 5. 判断任务结果
/// </summary>
public class MissionManager : MonoBehaviour
{

    public static MissionManager Instance;



    /// <summary>
    /// 固定任务模板库
    /// </summary>
    private Dictionary<string, MissionData> missionTemplates
        =
        new Dictionary<string, MissionData>();
    /// <summary>
    /// 根据任务类型获取任务列表
    /// 给UI入口使用
    /// </summary>
    public List<MissionData> GetMissionByType(MissionType type)
    {

        List<MissionData> result =
            new List<MissionData>();


        foreach (MissionData mission in missionTemplates.Values)
        {

            if (mission.missionType == type)
            {
                result.Add(mission);
            }

        }


        return result;

    }


    /// <summary>
    /// 当前正在进行的任务
    /// </summary>
    private List<Mission> activeMissions =
        new List<Mission>();


    public MissionNodePanel missionNodePanel;

    private void Awake()
    {

        if (Instance == null)
        {
            Instance = this;

            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

    }



    private void Start()
    {

        TimeManager.Instance.OnDayPassed += OnDayPassed;


        LoadMissionsFromJson();

    }




    /// <summary>
    /// 加载所有任务模板
    /// </summary>
    public void LoadMissionsFromJson()
    {

        missionTemplates.Clear();

        TextAsset[] jsonFiles =
            Resources.LoadAll<TextAsset>(
                "Configs/Missions"
            );

        if (jsonFiles.Length == 0)
        {
            Debug.LogError(
                "Mission Json不存在"
            );

            return;
        }

        foreach (TextAsset json in jsonFiles)
        {

            MissionData data =
                JsonConvert.DeserializeObject<MissionData>(
                    json.text
                );

            if (data == null)
            {
                Debug.LogError(
                    $"解析失败:{json.name}"
                );

                continue;
            }

            if (missionTemplates.ContainsKey(data.id))
            {
                Debug.LogError(
                    $"任务ID重复:{data.id}"
                );

                continue;
            }

            missionTemplates.Add(
                data.id,
                data
            );
            Debug.Log(
                $"加载任务模板:{data.id} {data.name}"
            );

        }
        Debug.Log(
            $"任务模板数量:{missionTemplates.Count}"
        );

    }


    /// <summary>
    /// 根据任务ID创建一个新的任务实例
    /// </summary>
    public Mission CreateMission(string missionId)
    {

        if (!missionTemplates.ContainsKey(missionId))
        {
            Debug.LogError(
                $"不存在任务模板:{missionId}"
            );

            return null;
        }

        Mission mission =
            new Mission(
                missionTemplates[missionId]
            );


        return mission;

    }
    /// <summary>
    /// 添加运行中的任务
    ///
    /// 给随机事件、剧情事件使用
    /// 因为这些任务不是从固定任务列表直接开始
    /// </summary>
    public void AddActiveMission(Mission mission)
    {

        if (mission == null)
        {
            Debug.LogWarning(
                "尝试加入空任务"
            );

            return;
        }


        if (!activeMissions.Contains(mission))
        {
            activeMissions.Add(mission);


            Debug.Log(
                $"加入运行任务:{mission.Data.name}"
            );
        }

    }
    /// <summary>
    /// 任务节点触发
    /// 通知UI显示
    /// </summary>
    public void OnMissionNodeTriggered(Mission mission)
    {

        if (missionNodePanel == null)
        {
            Debug.LogWarning(
            "没有绑定MissionNodePanel"
            );

            return;
        }


        missionNodePanel.Show(mission);

    }
    /// <summary>
    /// UI调用
    /// 开始任务
    /// </summary>
    public void TriggerMission(
        string missionId,
        NPCRuntime npc)
    {
        if (!npc.CanDispatch())
        {
            Debug.Log(
            $"{npc.Data.npcName} 当前无法执行任务"
            );

            return;
        }
        Mission mission =
            CreateMission(missionId);
        if (mission == null)
            return;
        mission.StartMission(npc);

        activeMissions.Add(mission); 
        Debug.Log(
        $"创建任务实例:{mission.Data.name}"
    );
        Debug.Log(
            $"开始任务:{mission.Data.name}"
        );

    }


    /// <summary>
    /// 任务完成判断
    /// </summary>
    public void EvaluateMission(Mission mission)
    {
        NPCRuntime npc =
            mission.AssignedNPC;

        MissionData data =
            mission.Data;


        if (npc == null)
        {

            Debug.LogWarning(
                "任务没有执行NPC"
            );

            return;

        }

        bool attackPass =
            npc.Attack >= data.requiredAttack;

        bool intelligencePass =
            npc.Intelligence >= data.requiredIntelligence;

        if (attackPass && intelligencePass)
        {

            Debug.Log(
                $"【{npc.Data.npcName}】成功完成任务：【{data.name}】"
            );

            mission.CompleteMission();

            NPCManager.Instance.Recover(npc);

            RewardManager.Instance.GiveReward(
                npc,
                mission.Reward
            );
            RemoveMission(mission);

        }
        else
        {

            Debug.Log(
                $"任务失败：【{npc.Data.npcName}】能力不足"
            );


            mission.FailMission();

        }

    }


    /// <summary>
    /// 删除已经结束的任务
    /// </summary>
    public void RemoveMission(
        Mission mission)
    {

        if (activeMissions.Contains(mission))
        {

            activeMissions.Remove(mission);

            if (mission.AssignedNPC != null &&
                mission.AssignedNPC.CurrentMission == mission &&
                mission.AssignedNPC.Character.IsAlive &&
                mission.AssignedNPC.State != NPCState.Injured)
            {
                NPCManager.Instance.Recover(mission.AssignedNPC);
            }

            Debug.Log(
                $"移除任务:{mission.Data.name}"
            );

        }

    }

    public IReadOnlyList<Mission> GetActiveMissions()
    {
        return activeMissions.AsReadOnly();
    }

    public void RestoreMissions(IEnumerable<MissionSaveData> savedMissions)
    {
        activeMissions.Clear();
        if (savedMissions == null) return;
        foreach (MissionSaveData saved in savedMissions)
        {
            MissionData data = GetMissionData(saved.missionId);
            NPCRuntime npc = NPCManager.Instance.GetRuntime(saved.assignedCharacterId);
            if (data == null || npc == null || !npc.Character.IsAlive)
            {
                Debug.LogWarning($"跳过无法恢复的任务: {saved.missionId}");
                continue;
            }
            activeMissions.Add(new Mission(data, saved, npc));
        }
    }


    /// <summary>
    /// 每天推进任务
    /// </summary>
    private void OnDayPassed(int currentDay)
    {
        //复制列表
        //避免任务完成后删除导致foreach异常
        List<Mission> missions =
            new List<Mission>(
                activeMissions
            );



        foreach (Mission mission in missions)
        {

            mission.PassOneDay();

        }

    }


    /// <summary>
    /// 获取任务模板
    ///
    /// 给UI显示使用
    /// </summary>
    public MissionData GetMissionData(
        string id)
    {

        if (
            missionTemplates.TryGetValue(
                id,
                out MissionData data
            ))
        {
            return data;
        }


        return null;

    }


    public void TestCreate()
    {
        Mission m = CreateMission("combat_001");

        Debug.Log(
            m == null ? "失败" : "成功创建"
        );
    }


    /// <summary>
    /// 获取所有任务模板
    ///
    /// 给任务列表使用
    /// </summary>
    public List<MissionData> GetMissionPool()
    {

        return new List<MissionData>(
            missionTemplates.Values
        );

    }




    private void OnDisable()
    {

        if (TimeManager.Instance != null)
        {

            TimeManager.Instance.OnDayPassed -= OnDayPassed;

        }

    }

}
