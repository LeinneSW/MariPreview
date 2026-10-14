using System;
using UnityEngine;

public static class MariMotionCatalog
{
    public static readonly string[] Groups = { "이동", "점프·착지", "전투", "카페", "스킬", "포즈·승리", "피격·퇴장" };

    public static readonly string[] ActionSections =
    {
        "기본", "무릎", "이동", "점프·착지", "카페", "스킬", "편성", "승리", "피격·퇴장", "공용", "기타"
    };

    private static readonly (string Clip, string Name)[] ActionLabels =
    {
        ("Normal_Idle", "대기"),
        ("Normal_Attack_Start", "공격 시작"),
        ("Normal_Attack_Ing", "공격"),
        ("Normal_Attack_End", "공격 마무리"),
        ("Normal_Attack_Delay", "공격 대기"),
        ("Normal_Reload", "장전"),
        ("Normal_Callsign", "호출"),
        ("Normal_Callsign_Random", "무작위 호출"),
        ("Kneel_Idle", "대기"),
        ("Kneel_Attack_Start", "공격 시작"),
        ("Kneel_Attack_Ing", "공격"),
        ("Kneel_Attack_End", "공격 마무리"),
        ("Kneel_Attack_Delay", "공격 대기"),
        ("Kneel_Reload", "장전"),
        ("Move_Ing", "이동"),
        ("Move_Jump", "점프"),
        ("Move_End_Normal", "기본 자세로 정지"),
        ("Move_End_Kneel", "무릎 자세로 정지"),
        ("Jump_Start", "시작"),
        ("Jump_Air", "공중"),
        ("Land", "착지"),
        ("Cafe_Idle", "대기"),
        ("Cafe_Walk", "걷기"),
        ("Cafe_Reaction", "반응"),
        ("Exs", "사용"),
        ("Exs_Cutin_01", "컷인 1"),
        ("Exs_Cutin_02", "컷인 2"),
        ("Exs_Cutin_03", "컷인 3"),
        ("Formation_Idle", "대기"),
        ("Formation_Pickup", "집어 들기"),
        ("Victory_Start", "시작"),
        ("Victory_End", "마무리"),
        ("Vital_Panic", "당황"),
        ("Vital_Dying_ing", "빈사"),
        ("Vital_Retreat", "퇴장"),
        ("Vital_Death", "사망"),
        ("Public01", "행위 1")
    };

    public static string ActionSection(string name)
    {
        if (name.StartsWith("Normal_", StringComparison.Ordinal)) return ActionSections[0];
        if (name.StartsWith("Kneel_", StringComparison.Ordinal)) return ActionSections[1];
        if (name.StartsWith("Move_", StringComparison.Ordinal)) return ActionSections[2];
        if (name.StartsWith("Jump_", StringComparison.Ordinal) || name == "Land") return ActionSections[3];
        if (name.StartsWith("Cafe_", StringComparison.Ordinal)) return ActionSections[4];
        if (name == "Exs" || name.StartsWith("Exs_", StringComparison.Ordinal)) return ActionSections[5];
        if (name.StartsWith("Formation_", StringComparison.Ordinal)) return ActionSections[6];
        if (name.StartsWith("Victory_", StringComparison.Ordinal)) return ActionSections[7];
        if (name.StartsWith("Vital_", StringComparison.Ordinal)) return ActionSections[8];
        if (name.StartsWith("Public", StringComparison.Ordinal)) return ActionSections[9];
        return ActionSections[10];
    }

    public static string ActionName(string name)
    {
        foreach (var action in ActionLabels)
            if (action.Clip == name) return action.Name;
        int separator = name.IndexOf('_');
        return (separator >= 0 ? name.Substring(separator + 1) : name).Replace('_', ' ');
    }

    public static int ActionOrder(string name)
    {
        for (int i = 0; i < ActionLabels.Length; i++)
            if (ActionLabels[i].Clip == name) return i;
        return int.MaxValue;
    }

    public static string Group(string name)
    {
        if (name == "Jump_Start" || name == "Jump_Air" || name == "Land" || name == "Move_Jump") return Groups[1];
        if (name.StartsWith("Cafe_", StringComparison.Ordinal)) return Groups[3];
        if (name.StartsWith("Exs", StringComparison.Ordinal)) return Groups[4];
        if (name.StartsWith("Vital_", StringComparison.Ordinal)) return Groups[6];
        if (name.Contains("_Attack_") || name.EndsWith("_Reload", StringComparison.Ordinal)) return Groups[2];
        if (name.StartsWith("Formation_", StringComparison.Ordinal) || name.StartsWith("Victory_", StringComparison.Ordinal)
            || name.StartsWith("Normal_Callsign", StringComparison.Ordinal) || name == "Public01") return Groups[5];
        return Groups[0];
    }

    public static string StatePath(string name) => "Base Layer." + Group(name) + "." + name;

    public static int ResolveState(Animator animator, string name, int layer = 0)
    {
        int grouped = Animator.StringToHash(StatePath(name));
        if (animator.HasState(layer, grouped)) return grouped;
        return Animator.StringToHash("Base Layer." + name);
    }
}
