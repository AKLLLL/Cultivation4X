using System;
using System.Collections.Generic;
using System.Linq;
using Cultivation4X.WorldMap;

public static class SectOrganizationRules
{
    public static SectDepartmentState DepartmentFor(PlayerData player, string discipleId) =>
        player?.departments?.FirstOrDefault(department => department?.memberDiscipleIds?.Contains(discipleId) == true);

    public static SectDepartmentState Get(PlayerData player, string departmentId) =>
        player?.departments?.FirstOrDefault(department => department?.departmentId == departmentId);

    public static bool TryCreate(PlayerData player, string requestedName,
        out SectDepartmentState department, out string reason)
    {
        department = null;
        if (player == null) { reason = "宗门数据不存在"; return false; }
        string name = NormalizeName(requestedName, out reason);
        if (name == null) return false;
        if (player.departments == null) player.departments = new List<SectDepartmentState>();
        if (player.departments.Any(item => item != null && string.Equals(item.name, name, StringComparison.Ordinal)))
        { reason = "部门名称已经存在"; return false; }
        if (player.nextDepartmentSequence < 1) player.nextDepartmentSequence = 1;
        string id;
        do id = $"department_{player.nextDepartmentSequence++:0000}";
        while (player.departments.Any(item => item?.departmentId == id));
        department = new SectDepartmentState
        {
            departmentId = id,
            name = name,
            type = DepartmentType.HerbCultivation
        };
        player.departments.Add(department);
        reason = null;
        return true;
    }

    public static bool TryRename(PlayerData player, string departmentId, string requestedName, out string reason)
    {
        SectDepartmentState department = Get(player, departmentId);
        if (department == null) { reason = "部门不存在"; return false; }
        string name = NormalizeName(requestedName, out reason);
        if (name == null) return false;
        if (player.departments.Any(item => item != null && item != department &&
            string.Equals(item.name, name, StringComparison.Ordinal)))
        { reason = "部门名称已经存在"; return false; }
        department.name = name;
        reason = null;
        return true;
    }

    public static bool TrySetMembers(PlayerData player, string departmentId,
        IEnumerable<string> discipleIds, IEnumerable<string> validLivingIds, out string reason)
    {
        SectDepartmentState department = Get(player, departmentId);
        if (department == null) { reason = "部门不存在"; return false; }
        HashSet<string> living = new HashSet<string>(validLivingIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        List<string> ids = (discipleIds ?? Enumerable.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (ids.Any(id => !living.Contains(id))) { reason = "成员必须是存活弟子"; return false; }
        if (player.departments.Any(other => other != null && other != department &&
            other.memberDiscipleIds != null && other.memberDiscipleIds.Any(ids.Contains)))
        { reason = "弟子只能属于一个主要部门"; return false; }
        department.memberDiscipleIds = ids;
        if (!string.IsNullOrWhiteSpace(department.leaderDiscipleId) &&
            !ids.Contains(department.leaderDiscipleId)) department.leaderDiscipleId = null;
        reason = null;
        return true;
    }

    public static bool TrySetLeader(PlayerData player, string departmentId, string discipleId, out string reason)
    {
        SectDepartmentState department = Get(player, departmentId);
        if (department == null) { reason = "部门不存在"; return false; }
        if (!string.IsNullOrWhiteSpace(discipleId) &&
            department.memberDiscipleIds?.Contains(discipleId) != true)
        { reason = "负责人必须先加入该部门"; return false; }
        department.leaderDiscipleId = string.IsNullOrWhiteSpace(discipleId) ? null : discipleId;
        reason = null;
        return true;
    }

    public static bool TryAssignZone(PlayerData player, WorldMapProgressState progress,
        string departmentId, string zoneId, out string reason)
    {
        SectDepartmentState department = Get(player, departmentId);
        SectFunctionalZoneState zone = progress?.functionalZones?.FirstOrDefault(item => item?.zoneId == zoneId);
        if (department == null || zone == null) { reason = "部门或功能区不存在"; return false; }
        if (!string.IsNullOrWhiteSpace(zone.assignedDepartmentId) && zone.assignedDepartmentId != departmentId)
        { reason = "必须先解除原负责部门"; return false; }
        zone.assignedDepartmentId = departmentId;
        reason = null;
        return true;
    }

    public static bool TryUnassignZone(WorldMapProgressState progress, string zoneId, out string reason)
    {
        SectFunctionalZoneState zone = progress?.functionalZones?.FirstOrDefault(item => item?.zoneId == zoneId);
        if (zone == null) { reason = "功能区不存在"; return false; }
        zone.assignedDepartmentId = null;
        reason = null;
        return true;
    }

    public static bool TryDelete(PlayerData player, WorldMapProgressState progress,
        string departmentId, out string reason)
    {
        SectDepartmentState department = Get(player, departmentId);
        if (department == null) { reason = "部门不存在"; return false; }
        foreach (SectFunctionalZoneState zone in progress?.functionalZones ?? new List<SectFunctionalZoneState>())
            if (zone?.assignedDepartmentId == departmentId) zone.assignedDepartmentId = null;
        player.departments.Remove(department);
        reason = null;
        return true;
    }

    public static void CleanupMembers(PlayerData player, IEnumerable<string> validLivingIds)
    {
        if (player?.departments == null) return;
        HashSet<string> valid = new HashSet<string>(validLivingIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        foreach (SectDepartmentState department in player.departments.Where(item => item != null))
        {
            department.memberDiscipleIds = (department.memberDiscipleIds ?? new List<string>())
                .Where(valid.Contains).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (!string.IsNullOrWhiteSpace(department.leaderDiscipleId) &&
                !department.memberDiscipleIds.Contains(department.leaderDiscipleId))
                department.leaderDiscipleId = null;
        }
    }

    private static string NormalizeName(string requestedName, out string reason)
    {
        string name = requestedName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length < 2 || name.Length > 12 || name.Any(char.IsControl))
        { reason = "部门名称应为2–12个字符且不能包含控制字符"; return null; }
        reason = null;
        return name;
    }
}
