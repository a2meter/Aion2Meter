# A2Meter / src / A2Meter / Calc

## Parent
../AGENTS.md

## Summary
Combat score calculation engine (distinct from DPS meter). Computes abyss/siege ranking scores and supplement effects. May be unused in current UI; preserved for future implementation.

## Key Files

| File | Purpose |
|------|---------|
| NativeCalcEngine.cs | P/Invoke to native DLL (possibly PacketEngine.dll) for formula computation |
| CombatScore.cs | Main combat score calculator; applies stat formulas, supplements |
| CombatScoreResult.cs | Result DTO (score value, component breakdown) |
| CalcInput.cs | Input parameters (stats, level, gear) |
| CalcResult.cs | Generic calculation result |
| Supplement.cs | Buff/supplement effect descriptor |
| SupplementResult.cs | Applied supplement effects |
| FormulaConfig.cs | Formula constants and configuration |

## AI Agent Instructions

- **Purpose**: Calculates "combat score" (아툴, combat power component) used for ranking/matchmaking, distinct from DPS.
- **Native Interop**: NativeCalcEngine likely calls PacketEngine.dll for performance-critical formula computation.
- **Unused in UI**: Currently, combat power/score displayed in overlay are fetched from Plaync API (SkillLevelCache); this module may be for future local calculation.
- **Supplements**: Models player stat buffs (food, potions); formula accounts for stacking rules.

## Dependencies

- A2Meter.Dps (player stats from meter)
- A2Meter.Dps.Protocol (game constants)
- System.Runtime.InteropServices (P/Invoke to native DLL)

## Notes

- If NativeCalcEngine is used, PacketEngine.dll must be present in bin\ or System32\.
- Formulas likely reverse-engineered from game client; values may change with patches.
