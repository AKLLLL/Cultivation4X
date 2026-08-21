using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public class CharacterStateTests
{
    [Test]
    public void Trait_IsUnique()
    {
        CharacterState state = new CharacterState();
        state.AddTrait("cautious");
        state.AddTrait("cautious");
        Assert.AreEqual(1, state.traitIds.Count);
    }

    [Test]
    public void DeadCharacter_IsNotAlive()
    {
        CharacterState state = new CharacterState { health = HealthState.Dead };
        Assert.IsFalse(state.IsAlive);
    }

    [Test]
    public void LifeRecord_PreservesSource()
    {
        CharacterState state = new CharacterState();
        state.AddLifeRecord(12, "Event", "经历了一次奇遇", "event_001");
        Assert.AreEqual("event_001", state.lifeRecords[0].sourceId);
    }

    [Test]
    public void GameState_RoundTripPreservesDeterministicEventState()
    {
        GameState source = new GameState
        {
            currentDay = 42,
            randomSeed = 1234,
            randomRollCount = 17,
            characters = new List<CharacterState>
            {
                new CharacterState { characterId = "c1", displayName = "测试弟子", traitIds = new List<string> { "cautious" } }
            }
        };
        GameState restored = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(source));
        Assert.AreEqual(42, restored.currentDay);
        Assert.AreEqual(17, restored.randomRollCount);
        Assert.AreEqual("cautious", restored.characters[0].traitIds[0]);
        Assert.AreEqual(DiscipleMentalStateRules.MaxMentalState, restored.characters[0].mentalState);
    }

    [Test]
    public void CharacterEventReferences_AreValid()
    {
        List<EventDefinition> events = new List<EventDefinition>();
        foreach (TextAsset file in Resources.LoadAll<TextAsset>("Configs/CharacterEvents"))
        {
            if (file.text.TrimStart().StartsWith("["))
                events.AddRange(JsonConvert.DeserializeObject<List<EventDefinition>>(file.text));
            else
                events.Add(JsonConvert.DeserializeObject<EventDefinition>(file.text));
        }

        HashSet<string> eventIds = events.Select(item => item.id).ToHashSet();
        HashSet<string> traitIds = JsonConvert.DeserializeObject<List<TraitDefinition>>(
            Resources.Load<TextAsset>("Configs/Traits/traits").text).Select(item => item.id).ToHashSet();
        HashSet<string> itemIds = Resources.LoadAll<TextAsset>("Configs/Items")
            .Select(file => JsonConvert.DeserializeObject<ItemData>(file.text).itemId).ToHashSet();
        HashSet<string> npcIds = Resources.LoadAll<NPCData>("NPC").Select(item => item.npcID).ToHashSet();

        Assert.GreaterOrEqual(events.Count, 20);
        Assert.AreEqual(events.Count, eventIds.Count, "事件 ID 必须唯一");
        foreach (EventEffect effect in events.SelectMany(item => item.options).SelectMany(item => item.outcomes).SelectMany(item => item.effects))
        {
            if (effect.type == EventEffectType.ScheduleEvent) Assert.Contains(effect.value, eventIds.ToList());
            if (effect.type == EventEffectType.AddTrait || effect.type == EventEffectType.PermanentTrauma) Assert.Contains(effect.value, traitIds.ToList());
            if (effect.type == EventEffectType.AddItem || effect.type == EventEffectType.RemoveItem) Assert.Contains(effect.value, itemIds.ToList());
            if (effect.type == EventEffectType.Recruit) Assert.Contains(effect.value, npcIds.ToList());
        }
    }
}
