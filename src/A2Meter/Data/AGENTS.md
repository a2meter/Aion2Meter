# A2Meter / src / A2Meter / Data

## Parent
../AGENTS.md

## Summary
Game database access. Manages skill/item/NPC data required for combat parsing. Data downloaded on first launch; stored locally for offline use.

## Key Files

| File | Purpose |
|------|---------|
| DataManager.cs | Singleton; checks if game database is ready; coordinates downloads on first launch |
| GameDatabase.cs | In-memory skill/item/NPC lookup; possibly generated from downloaded JSON or embedded resource |

## AI Agent Instructions

- **First Launch**: Program.NeedsSetup() checks DataManager.IsReady; if false, shows SetupForm.
- **Data Source**: Likely downloaded from A2Web or remote server during setup (exact mechanism undocumented).
- **Lookups**: Protocol parsers and DpsPipeline query game data to resolve skill/item IDs to readable names.
- **Offline Use**: Once downloaded, data is cached locally; app functions without network (except skill level API calls).

## Dependencies

- System.Text.Json (if data stored as JSON)
- System.Net.Http (download on setup)
- A2Meter.Core (AppSettings for storage path)

## Notes

- Data likely includes: skill ID → name/category, item ID → name, NPC ID → name, dungeon ID → name/properties.
