using System;
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
    public CharacterState Character { get; private set; }
    public NPCRuntime(NPCData data)
    {
        Data = data;
        Character = new CharacterState
        {
            characterId = string.IsNullOrWhiteSpace(data.npcID) ? Guid.NewGuid().ToString("N") : data.npcID,
            templateId = data.npcID,
            displayName = data.npcName,
            age = data.age,
            level = data.level,
            exp = data.exp,
            traitIds = new System.Collections.Generic.List<string>(data.initialTraits)
        };
        //读取初始值
        Level = data.level;
        Exp = data.exp;
        Gold = 0;

        State = NPCState.Idle;

        StateRemainDays = 0;
    }

    public NPCRuntime(NPCData data, CharacterState state)
    {
        Data = data;
        Character = state;
        Level = state.level;
        Exp = state.exp;
        Gold = 0;
        State = state.activityState;
        StateRemainDays = state.stateRemainDays;
    }

    public string CharacterId => Character.characterId;
    public HealthState Health => Character.health;
    public CultivationRealm Realm => Character.realm;
    public int Cultivation => Character.cultivation;
    public int Attack
    {
        get
        {
            int modifier = 0;
            if (TraitDatabase.Instance != null)
                foreach (string id in Character.traitIds)
                    modifier += TraitDatabase.Instance.Get(id)?.attackModifier ?? 0;
            return Data.attack + modifier;
        }
    }

    public int Intelligence
    {
        get
        {
            int modifier = 0;
            if (TraitDatabase.Instance != null)
                foreach (string id in Character.traitIds)
                    modifier += TraitDatabase.Instance.Get(id)?.intelligenceModifier ?? 0;
            return Data.intelligence + modifier;
        }
    }
    public int Agility => Data.agility;
    public int Comprehension => Data.comprehension;
    public int CombatComprehension => Data.combatComprehension > 0 ? Data.combatComprehension : Data.comprehension;
    public int Physique => Data.physique;
    public int CombatExperience => Character.combatExperience;
    public int CombatPower => CharacterCapabilityRules.CalculateCombatPower(this);
    public int AptitudeRank => Character.aptitudeRank;
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
        Character.activityState = state;
        Character.stateRemainDays = days;
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
        Character.activityState = State;
        Character.stateRemainDays = StateRemainDays;
        Character.level = Level;
        Character.exp = Exp;
    }


    public bool CanDispatch()
    {
        return Character.IsAlive && State == NPCState.Idle;
    }

    public void AddCultivation(int amount)
    {
        if (!Character.IsAlive || amount <= 0) return;
        Character.cultivation += amount;
    }

    public void AddCombatExperience(int amount)
    {
        if (!Character.IsAlive || amount <= 0) return;
        Character.combatExperience = Mathf.Max(0, Character.combatExperience + amount);
    }

    public bool TryBreakthrough(float bonusChance = 0f)
    {
        int need = Character.realm == CultivationRealm.Mortal || Character.realm == CultivationRealm.QiRefining ? 100 : 300;
        if (!Character.IsAlive || Character.realm == CultivationRealm.GoldenCore || Character.cultivation < need)
            return false;

        float healthPenalty = Character.health == HealthState.PermanentTrauma ? 0.2f : 0f;
        float chance = Mathf.Clamp01(0.65f + bonusChance - healthPenalty);
        Character.cultivation -= need;
        if (UnityEngine.Random.value > chance) return false;

        Character.realm = (CultivationRealm)((int)Character.realm + 1);
        Character.AddLifeRecord(TimeManager.Instance == null ? 0 : TimeManager.Instance.CurrentDay,
            "Breakthrough", $"突破至 {Character.realm}");
        return true;
    }
}
