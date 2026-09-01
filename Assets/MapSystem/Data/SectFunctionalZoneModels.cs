using System;

namespace Cultivation4X.WorldMap
{
    public enum FunctionalZoneType
    {
        HerbCultivation = 0
    }

    public enum FunctionalZoneStage
    {
        Planned = 0,
        Developing = 1,
        Operational = 2
    }

    [Serializable]
    public sealed class SectFunctionalZoneState
    {
        public string zoneId;
        public int cellIndex = -1;
        public FunctionalZoneType type = FunctionalZoneType.HerbCultivation;
        public FunctionalZoneStage stage = FunctionalZoneStage.Planned;
        public float phaseProgress;
        public float harvestProgress;
        public string assignedDepartmentId;
    }
}
