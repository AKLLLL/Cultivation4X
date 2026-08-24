using System;
using UnityEngine;

public class NPCRuntime
{
    // 对应静态数据
    public NPCData Data;
    //-----------------
    //成长数据
    //-----------------

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
            realmLayer = 1,
            techniqueMastery = 50f,
            spiritRoot = FoundingRules.GenerateSpiritRoot(new System.Random(StableSeed(data.npcID))),
            traitIds = new System.Collections.Generic.List<string>(data.initialTraits)
        };
        //读取初始值
        State = NPCState.Idle;

        StateRemainDays = 0;
    }

    public NPCRuntime(NPCData data, CharacterState state)
    {
        Data = data;
        Character = state;
        State = state.activityState;
        StateRemainDays = state.stateRemainDays;
    }

    public string CharacterId => Character.characterId;
    public HealthState Health => Character.health;
    public CultivationRealm Realm => Character.realm;
    public float CurrentAura => Character.currentAura;
    public int RealmLayer => Character.realmLayer;
    public int MentalState => Character.mentalState;
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
    public SpiritRootQuality SpiritRootQuality => Character.spiritRoot?.quality ?? SpiritRootQuality.Medium;
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
    }


    public bool CanDispatch()
    {
        return Character.IsAlive && State == NPCState.Idle;
    }

    public float AddAura(float amount) => DailyCultivationSimulator.AddAura(this, amount);
    public float AddAuraControl(float amount) => DailyCultivationSimulator.AddAuraControl(this, amount);
    public void AddTechniqueMastery(float amount) => DailyCultivationSimulator.AddTechniqueMastery(this, amount);

    public void ChangeMentalState(int amount)
    {
        if (!Character.IsAlive || amount == 0) return;
        Character.mentalState = Mathf.Clamp(Character.mentalState + amount,
            DiscipleMentalStateRules.MinMentalState, DiscipleMentalStateRules.MaxMentalState);
    }

    public void AddCombatExperience(int amount)
    {
        if (!Character.IsAlive || amount <= 0) return;
        Character.combatExperience = Mathf.Max(0, Character.combatExperience + amount);
    }

    private static int StableSeed(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in value ?? string.Empty) hash = hash * 31 + c;
            return hash;
        }
    }
}
