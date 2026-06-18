using UnityEngine;
using System.Collections;

/// <summary>
/// Base class for all power-ups. Inherit from this to create new power-ups.
/// </summary>
public abstract class PowerUp : MonoBehaviour
{
    public abstract void Activate();
    public abstract string PowerUpName { get; }
    public float duration = 5f;
}

// ─────────────────────────────────────────────────
// SHIELD — Makes the player invincible briefly
// ─────────────────────────────────────────────────
public class ShieldPowerUp : PowerUp
{
    public override string PowerUpName => "Shield";

    public override void Activate()
    {
        PowerUpManager.Instance.ActivateShield(duration);
    }
}

// ─────────────────────────────────────────────────
// COIN MAGNET — Attracts nearby coins
// ─────────────────────────────────────────────────
public class MagnetPowerUp : PowerUp
{
    public override string PowerUpName => "Magnet";

    public override void Activate()
    {
        PowerUpManager.Instance.ActivateMagnet(duration);
    }
}

// ─────────────────────────────────────────────────
// SLOW MO — Slows game speed temporarily
// ─────────────────────────────────────────────────
public class SlowMoPowerUp : PowerUp
{
    public override string PowerUpName => "Slow Mo";

    public override void Activate()
    {
        PowerUpManager.Instance.ActivateSlowMo(duration);
    }
}

// ─────────────────────────────────────────────────
// X2 COINS — Doubles coin value
// ─────────────────────────────────────────────────
public class CoinMultiplierPowerUp : PowerUp
{
    public int multiplier = 2;
    public override string PowerUpName => $"x{multiplier} Coins";

    public override void Activate()
    {
        PowerUpManager.Instance.ActivateCoinMultiplier(multiplier, duration);
    }
}
