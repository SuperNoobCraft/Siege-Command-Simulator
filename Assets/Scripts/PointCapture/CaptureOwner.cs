using UnityEngine;

/// <summary>
/// Point-capture sides. Yellow is friendly (own prefabs), Red is foe (enemy prefabs).
/// </summary>
public enum CaptureOwner
{
    Neutral = 0,
    Red = 1,
    Yellow = 2
}

public static class CaptureTeams
{
    public static readonly Color Red = new Color(0.86f, 0.16f, 0.12f, 1f);
    public static readonly Color Yellow = new Color(0.95f, 0.78f, 0.12f, 1f);
    public static readonly Color Neutral = new Color(0.62f, 0.64f, 0.68f, 1f);

    public static Color GetColor(CaptureOwner owner, float alpha = 1f)
    {
        Color color = owner switch
        {
            CaptureOwner.Red => Red,
            CaptureOwner.Yellow => Yellow,
            _ => Neutral
        };
        color.a = alpha;
        return color;
    }

    public static string GetDisplayName(CaptureOwner owner)
    {
        return owner switch
        {
            CaptureOwner.Red => "Red",
            CaptureOwner.Yellow => "Yellow",
            _ => "Neutral"
        };
    }

    public static TroopCombat.Faction ToTroopFaction(CaptureOwner owner)
    {
        return owner == CaptureOwner.Red ? TroopCombat.Faction.Enemy : TroopCombat.Faction.Friendly;
    }

    public static CaptureOwner FromTroopFaction(TroopCombat.Faction faction)
    {
        return faction == TroopCombat.Faction.Enemy ? CaptureOwner.Red : CaptureOwner.Yellow;
    }

    public static CaptureOwner Opposite(CaptureOwner owner)
    {
        return owner switch
        {
            CaptureOwner.Red => CaptureOwner.Yellow,
            CaptureOwner.Yellow => CaptureOwner.Red,
            _ => CaptureOwner.Neutral
        };
    }

    public static bool IsPlayerSide(CaptureOwner owner)
    {
        return owner == CaptureOwner.Red || owner == CaptureOwner.Yellow;
    }

    /// <summary>
    /// Neutralize phase: owner → gray. Occupy phase: gray → attacker.
    /// </summary>
    public static Color GetSiegeDiscColor(
        CaptureOwner owner,
        CaptureOwner attacker,
        float captureProgress,
        float alpha = 0.45f)
    {
        Color ownerColor = GetColor(owner, alpha);
        Color attackerColor = GetColor(attacker, alpha);
        Color neutralColor = GetColor(CaptureOwner.Neutral, alpha);
        float t = Mathf.Clamp01(captureProgress);

        if (attacker == CaptureOwner.Neutral || t <= 0.001f)
        {
            return ownerColor;
        }

        if (owner == CaptureOwner.Neutral)
        {
            return Color.Lerp(neutralColor, attackerColor, t);
        }

        return Color.Lerp(ownerColor, neutralColor, t);
    }
}
