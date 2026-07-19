using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;


public class MissionDatabase : MonoBehaviour
{

    public static MissionDatabase Instance;


    private Dictionary<string, MissionData> missions
        =
        new Dictionary<string, MissionData>();


    private void Awake()
    {

        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Load();
        }
        else
        {
            Destroy(gameObject);
        }

    }



    public void Load()
    {

        TextAsset[] files =
        Resources.LoadAll<TextAsset>(
            "Configs/Missions"
        );


        foreach (var file in files)
        {

            MissionData data =
            JsonConvert.DeserializeObject<MissionData>(
                file.text
            );


            if (data == null)
                continue;


            missions.Add(
                data.id,
                data
            );


            Debug.Log(
            $"加载任务:{data.name}"
            );

        }


        Debug.Log(
        $"任务数量:{missions.Count}"
        );

    }



    public MissionData GetMission(string id)
    {

        if (
        missions.TryGetValue(
            id,
            out MissionData data
        ))
        {
            return data;
        }


        return null;

    }



    public List<MissionData> GetAll()
    {

        return
        new List<MissionData>(
            missions.Values
        );

    }



    public List<MissionData> GetByType(
        MissionType type)
    {

        List<MissionData> result =
            new List<MissionData>();


        foreach (var mission in missions.Values)
        {

            if (mission.missionType == type)
            {
                result.Add(mission);
            }

        }


        return result;

    }

}