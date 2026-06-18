# Jetpack Joyride Clone — Unity Setup Guide

## Unity Version
Tested with Unity 2022 LTS or newer (2D URP or Built-in RP both work).
Requires **TextMeshPro** package (install via Package Manager).

---

## Scene Hierarchy

```
📁 Scene: GameScene
├── 🎥 Main Camera
│   └── CameraShake.cs
│
├── 🎮 GameManager (Empty GameObject)
│   ├── GameManager.cs
│   ├── ScoreManager.cs
│   └── PowerUpManager.cs
│
├── 👤 Player
│   ├── Sprite: "Barry" character sprite
│   ├── Rigidbody2D (Gravity Scale: 0, Constraints: Freeze Pos X & Rot Z)
│   ├── CapsuleCollider2D
│   ├── PlayerController.cs
│   ├── Animator
│   ├── AudioSource
│   └── 🌟 JetpackParticles (ParticleSystem child)
│
├── 🌄 Background
│   └── BackgroundScroller.cs
│   ├── Layer_Far (parallaxFactor: 0.3, tileWidth: 20)
│   │   ├── BgTile_Far_1
│   │   └── BgTile_Far_2
│   ├── Layer_Mid (parallaxFactor: 0.6, tileWidth: 20)
│   │   ├── BgTile_Mid_1
│   │   └── BgTile_Mid_2
│   └── Layer_Near (parallaxFactor: 1.0, tileWidth: 20)
│       ├── Floor_1
│       └── Floor_2
│
├── 🚧 ObstacleSpawner (Empty GameObject)
│   └── ObstacleSpawner.cs
│       Obstacle prefabs:
│       ├── Zapper (weight: 1.0)
│       ├── Laser (weight: 0.8)
│       └── Missile (weight: 0.6)
│
├── 🪙 CoinSpawner (Empty GameObject)
│   └── CoinSpawner.cs
│
└── 🖥️ UI (Canvas — Screen Space Overlay)
    ├── HUD
    │   ├── DistanceText (TMP)
    │   ├── CoinText (TMP)
    │   └── BestText (TMP)
    ├── PowerUpUI
    │   ├── ActivePowerUpText (TMP)
    │   └── TimerBar (Image — Filled)
    ├── MilestonePopup (disabled by default)
    │   └── MilestoneText (TMP)
    ├── StartPanel
    │   ├── TitleText
    │   ├── StartButton → GameManager.StartGame()
    │   └── CountdownText (TMP)
    └── GameOverPanel (disabled by default)
        ├── FinalDistanceText (TMP)
        ├── FinalCoinsText (TMP)
        ├── BestDistanceText (TMP)
        └── RestartButton → GameManager.RestartGame()
```

---

## Prefabs to Create

### 1. Player
- Sprite: simple character sprite (or placeholder rectangle)
- Tag: **Player**, Layer: **Player**
- Rigidbody2D: Gravity Scale = 0, freeze X position & Z rotation
- CapsuleCollider2D (Is Trigger: ON for coin/powerup, separate physics collider for floor)
- Add PlayerController.cs

### 2. Coin Prefab
- Sprite: gold circle
- Tag: **Coin**, Layer: **Coin**
- CircleCollider2D (Is Trigger: ON)
- CoinMover.cs

### 3. Zapper Prefab
- Sprite: electric bar (horizontal or diagonal)
- Tag: **Obstacle**, Layer: **Obstacle**
- BoxCollider2D
- ZapperObstacle.cs
- Create several rotated variants (0°, 45°, 90°)

### 4. Laser Prefab
- Sprite: long horizontal red/orange bar
- Tag: **Obstacle**
- BoxCollider2D (disabled on start — LaserObstacle.cs handles enabling)
- LaserObstacle.cs
- Child: WarningGlow (separate SpriteRenderer)

### 5. Missile Prefab
- Sprite: missile/rocket pointing right
- Tag: **Obstacle**
- CapsuleCollider2D
- GuidedMissile.cs
- Child: ExhaustParticles (ParticleSystem)
- Child: WarningIndicator (SpriteRenderer — "!" sign)

---

## Layers & Physics Matrix

Create these layers in Edit > Project Settings > Tags & Layers:
- **Player** (Layer 6)
- **Obstacle** (Layer 7)
- **Coin** (Layer 8)

In Physics2D matrix:
- Player ↔ Obstacle: ON
- Player ↔ Coin: ON (trigger)
- Obstacle ↔ Coin: OFF
- Obstacle ↔ Obstacle: OFF

---

## GameManager Inspector Assignments

```
Player:              → Player GameObject
ObstacleSpawner:     → ObstacleSpawner GameObject
BackgroundScroller:  → Background GameObject
CoinSpawner:         → CoinSpawner GameObject

UI:
  DistanceText:      → HUD/DistanceText
  CoinText:          → HUD/CoinText
  BestDistanceText:  → HUD/BestText
  GameOverPanel:     → UI/GameOverPanel
  FinalDistanceText: → GameOverPanel/FinalDistanceText
  FinalCoinsText:    → GameOverPanel/FinalCoinsText
  StartPanel:        → UI/StartPanel
  CountdownText:     → StartPanel/CountdownText
```

---

## Quick Start Tips

1. **Camera**: Set to Orthographic, Size: 5, Position: (0, 0, -10)
2. **Player Start Position**: (-3, 0, 0)
3. **Floor Y**: -3.5, **Ceiling Y**: 3.5
4. **Background tiles**: Make them 20 units wide, place two side by side
5. **Test with placeholder sprites first** — use Unity's built-in white rectangles

---

## Adding More Obstacles

1. Create a prefab with a sprite, collider (Tag: **Obstacle**)
2. Add `ObstacleMover.cs` (or a custom script extending it)
3. Drag into `ObstacleSpawner.obstacles[]` array with a weight value

---

## Sound Effects (free sources)
- freesound.org: search "jetpack", "laser zap", "missile", "coin collect"
- kenney.nl: free game asset packs including SFX

---

## Recommended Art Assets (Free)
- **Kenney.nl** Space Shooter / Platformer packs
- **OpenGameArt.org** — sci-fi/lab themed sprites
- Unity Asset Store: "Jetpack Character" free packs

---

## Script Summary

| Script | Purpose |
|---|---|
| PlayerController.cs | Jetpack physics, input, death/revive |
| GameManager.cs | Game state, speed, coins, UI |
| ObstacleSpawner.cs | Object pooling + timed obstacle spawn |
| ObstacleMover.cs | Moves obstacles left, despawns off-screen |
| CoinSpawner.cs | Spawns coins in patterns (arc, line, cluster) |
| CoinMover.cs | Moves coins left, bobbing animation |
| LaserObstacle.cs | Warning → active laser with flickering |
| ZapperObstacle.cs | Electric obstacle that toggles on/off |
| GuidedMissile.cs | Homing missile with warning phase |
| PowerUp.cs | Base class + Shield, Magnet, SlowMo, x2Coins |
| PowerUpManager.cs | Manages active power-up effects |
| BackgroundScroller.cs | Infinite parallax background scrolling |
| ScoreManager.cs | Milestones, distance multipliers |
| Utilities.cs | CameraShake, FloatingText, BoundaryKill |
