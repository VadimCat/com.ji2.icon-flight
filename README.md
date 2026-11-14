# JI2 Icon Flight

A pooled uGUI service for the common casual-game moment where a collected thing flies to its HUD counter: a goal object to its goal plate, a coin to the wallet, a reward to its slot. It needs only `com.unity.ugui`; there is no tween library.

## Use

```csharp
var layer = /* full-screen RectTransform above the HUD, no raycasts */;
var flights = new IconFlightService(layer) { Instant = reducedMotion };

// When the game state has already counted the pickup:
flights.LaunchFromWorld(item.Icon, pickupWorldPoint, worldCamera, goalPlate,
    landed: () => ShowCount(goalIndex), delay: stagger);

// Every frame, with the clock the HUD uses (usually unscaled):
flights.Tick(Time.unscaledDeltaTime);

// Screen rebuilt or level restarted: drop flights without callbacks.
flights.CancelAll();
// Results about to cover the HUD: land everything now.
flights.CompleteAll();
```

- `LaunchFromScreen`, `LaunchFromWorld` and `Launch` (layer-local point) start a flight. The target is followed while it moves.
- `IconFlightStyle` sets start size, pop, flight time, arc bow, landing size relative to the target and an optional target scale punch. Set `TargetPunch = 0` when the target already animates its own scale.
- `Instant` lands launches at once (reduced motion). The callback still runs, so counts stay correct.
- `IconFlightPath` holds the pure arc and easing functions.

## Rules

- Presentation only. Count the domain state at pickup; use the landing callback to update what the HUD shows.
- The owner ticks the service and cancels it with the screen that owns the targets.
- Keep game-specific icons, targets, counters and sounds in the game.

## Provenance

Extracted from Snowball Sweep goal flights (2026-11-14); consumer regression test: Snowball Sweep `GamePlayTests.GoalIconFliesToItsPlateAndCountsOnLanding`. Same pattern as the goal-face flights in PushColorMerge (`LevelGoalStripView`) and the reward flight in Sequence (`RewardFlightAnimationService`), both of which use DOTween.
