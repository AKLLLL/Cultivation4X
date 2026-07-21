using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class NPCManager : MonoBehaviour
{

    public static NPCManager Instance;


    private Dictionary<NPCData, NPCRuntime> npcMap
        = new Dictionary<NPCData, NPCRuntime>();

    private readonly Dictionary<string, NPCRuntime> npcById =
        new Dictionary<string, NPCRuntime>();
    private readonly List<NPCRuntime> runtimes = new List<NPCRuntime>();

    public List<NPCRuntime> GetAllNPC()
    {
        return new List<NPCRuntime>(runtimes);
    }

    public List<NPCRuntime> GetLivingNPC()
    {
        return runtimes.Where(npc => npc.Character.IsAlive).ToList();
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
        npcMap.Clear();
        npcById.Clear();
        runtimes.Clear();
        NPCData[] npcs = Resources.LoadAll<NPCData>("NPC");

        

        foreach (var npc in npcs)
        {
            NPCRuntime runtime = new NPCRuntime(npc);

            npcMap.Add(npc, runtime);
            npcById[runtime.CharacterId] = runtime;
            runtimes.Add(runtime);
           
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

    public void SetState(NPCRuntime runtime, NPCState state, int days = 0)
    {
        if (runtime == null) return;
        runtime.SetState(state, days);
    }
    //开始任务
    public void StartMission(NPCData npc, Mission mission)
    {
        NPCRuntime runtime = GetRuntime(npc);


        if (runtime == null)
            return;


        runtime.SetState(NPCState.Busy);

        runtime.CurrentMission = mission;
    }

    public void StartMission(NPCRuntime runtime, Mission mission)
    {
        if (runtime == null || !runtime.CanDispatch()) return;
        runtime.SetState(NPCState.Busy);
        runtime.CurrentMission = mission;
    }


    public void Injured(NPCRuntime npc, int days)
    {
        if (npc == null || !npc.Character.IsAlive) return;
        npc.Character.health = days >= 5 ? HealthState.SeriousInjury : HealthState.LightInjury;
        npc.Character.AddLifeRecord(CurrentDay, "Injury", $"受伤，需要休养 {days} 天");
        SetState(npc, NPCState.Injured, days);
    }

    //恢复空闲状态
    public void Recover(NPCRuntime npc)
    {
        NPCRuntime runtime = npc;
        if (runtime == null)
            return;


        runtime.SetState(NPCState.Idle, 0);
        if (runtime.Character.health != HealthState.PermanentTrauma)
            runtime.Character.health = HealthState.Healthy;

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

    public NPCRuntime GetRuntime(string characterId)
    {
        npcById.TryGetValue(characterId, out NPCRuntime runtime);
        return runtime;
    }

    public bool AddRelationship(string sourceId, string targetId, RelationshipTag tag)
    {
        NPCRuntime source = GetRuntime(sourceId);
        if (source == null || GetRuntime(targetId) == null || sourceId == targetId) return false;
        if (source.Character.relationships.Any(r => r.targetCharacterId == targetId && r.tag == tag)) return false;
        source.Character.relationships.Add(new RelationshipRecord
        {
            sourceCharacterId = sourceId,
            targetCharacterId = targetId,
            tag = tag,
            createdDay = CurrentDay
        });
        return true;
    }

    public bool Kill(NPCRuntime npc, string cause)
    {
        if (npc == null || !npc.Character.IsAlive) return false;
        // 防止全员死亡坏档：最后一名弟子转为永久创伤。
        if (GetLivingNPC().Count <= 1)
        {
            npc.Character.health = HealthState.PermanentTrauma;
            npc.Character.AddTrait("near_death_survivor");
            npc.Character.AddLifeRecord(CurrentDay, "NearDeath", $"死里逃生：{cause}");
            Recover(npc);
            return false;
        }

        npc.Character.health = HealthState.Dead;
        npc.Character.activityState = NPCState.Idle;
        npc.State = NPCState.Idle;
        Mission mission = npc.CurrentMission;
        npc.CurrentMission = null;
        npc.Character.AddLifeRecord(CurrentDay, "Death", cause);
        if (mission != null) MissionManager.Instance?.RemoveMission(mission);
        return true;
    }

    public NPCRuntime RecruitFromTemplate(string templateId)
    {
        NPCData template = Resources.LoadAll<NPCData>("NPC")
            .FirstOrDefault(data => data.npcID == templateId);
        if (template == null) return null;

        CharacterState state = new CharacterState
        {
            characterId = $"{templateId}_{System.Guid.NewGuid():N}",
            templateId = templateId,
            displayName = template.npcName,
            age = template.age,
            level = template.level,
            exp = template.exp,
            traitIds = new List<string>(template.initialTraits)
        };
        NPCRuntime runtime = new NPCRuntime(template, state);
        if (!npcMap.ContainsKey(template)) npcMap[template] = runtime;
        npcById[state.characterId] = runtime;
        runtimes.Add(runtime);
        state.AddLifeRecord(CurrentDay, "Recruit", "加入宗门");
        return runtime;
    }



    /// <summary>
    /// 每天推进
    /// </summary>
    public void OnDayPassed()
    {
        foreach (var npc in runtimes)
        {
            npc.OnDayPassed();
            if (npc.Character.IsAlive && npc.State == NPCState.Idle)
            {
                int amount = Mathf.Max(1, PlayerManager.Instance.playerData.trainingRoomLevel);
                if (npc.Character.HasTrait("diligent")) amount += 1;
                if (npc.Character.HasTrait("lazy")) amount = Mathf.Max(1, amount - 1);
                npc.AddCultivation(amount);
            }
        }
    }

    private int CurrentDay => TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay;

    public void RestoreCharacters(IEnumerable<CharacterState> states)
    {
        npcMap.Clear();
        npcById.Clear();
        runtimes.Clear();
        Dictionary<string, NPCData> templates = Resources.LoadAll<NPCData>("NPC")
            .Where(item => !string.IsNullOrWhiteSpace(item.npcID))
            .GroupBy(item => item.npcID)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (CharacterState state in states ?? Enumerable.Empty<CharacterState>())
        {
            if (!templates.TryGetValue(state.templateId, out NPCData template))
            {
                Debug.LogWarning($"存档角色缺少模板: {state.templateId}");
                continue;
            }
            NPCRuntime runtime = new NPCRuntime(template, state);
            if (!npcMap.ContainsKey(template)) npcMap[template] = runtime;
            npcById[state.characterId] = runtime;
            runtimes.Add(runtime);
        }
    }


}
