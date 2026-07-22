using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// 随机任务生成器
/// 负责从任务池中生成随机任务
/// </summary>
public class RandomEventManager : MonoBehaviour
{

    public static RandomEventManager Instance;

    //事件模板
    private Dictionary<string, MissionData> eventTemplates
        =
        new Dictionary<string, MissionData>();
    /// <summary>
    /// 所有任务配置
    /// 来自Json读取后的任务池
    /// </summary>
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            DontDestroyUtility.MarkPersistent(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    private void Start()
    {
        LoadEvents();
    }
    /// <summary>
    /// 加载事件JSON
    /// </summary>
    private void LoadEvents()
    {
        eventTemplates.Clear();

        TextAsset[] files =
        Resources.LoadAll<TextAsset>(
            "Configs/Events"
        );

        foreach (TextAsset file in files)
        {

            MissionData data =
            JsonConvert.DeserializeObject<MissionData>(
                file.text
            );

            if (data == null)
                continue;

            eventTemplates.Add(
                data.id,
                data
            );

            Debug.Log(
            $"加载随机事件:{data.name}"
            );

        }

        Debug.Log(
        $"随机事件数量:{eventTemplates.Count}"
        );
    }
    public void TriggerRandomEvent(
       NPCRuntime npc
   )
    {

        if (eventTemplates.Count == 0)
            return;

        List<MissionData> list =
            new List<MissionData>(
                eventTemplates.Values
            );

        MissionData data =
            list[
            Random.Range(
                0,
                list.Count
            )
            ];

        CreateEvent(
            data,
            npc
        );

    }
    /// <summary>
    /// 创建事件任务
    /// </summary>
    private void CreateEvent(
        MissionData data,
        NPCRuntime npc
    )
    {

        Mission mission =
            new Mission(data);

        mission.StartMission(npc);

        MissionManager.Instance
        .AddActiveMission(
            mission
        );

        Debug.Log(
        $"触发随机事件:{data.name}"
        );

    }
    /// <summary>
    /// 根据ID触发指定事件
    /// 给TriggerEvent使用
    /// </summary>
    public void TriggerEvent(
        string id,
        NPCRuntime npc
    )
    {

        if (!eventTemplates.ContainsKey(id))
        {
            Debug.LogWarning(
            $"不存在事件:{id}"
            );

            return;
        }

        CreateEvent(
            eventTemplates[id],
            npc
        );
    }
    public void TestRandomEvent()
    {
        NPCRuntime npc =
            NPCManager.Instance.GetAllNPC()[0];


        RandomEventManager.Instance
        .TriggerRandomEvent(npc);
    }
}
