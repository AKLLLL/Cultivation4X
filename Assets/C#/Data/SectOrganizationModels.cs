using System;
using System.Collections.Generic;

public enum DepartmentType
{
    HerbCultivation = 0
}

[Serializable]
public sealed class SectDepartmentState
{
    public string departmentId;
    public string name;
    public DepartmentType type = DepartmentType.HerbCultivation;
    public string leaderDiscipleId;
    public List<string> memberDiscipleIds = new List<string>();
}
