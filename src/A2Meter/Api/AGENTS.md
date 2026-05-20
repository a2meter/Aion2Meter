# A2Meter / src / A2Meter / Api

## Parent
../AGENTS.md

## Summary
Web API clients for character enrichment and combat record upload. Fetches skill levels, combat power, and combat score from Plaync API; uploads combat records to A2Web statistics server.

## Key Files

| File | Purpose |
|------|---------|
| WebUploader.cs | Uploads CombatRecord to A2Web server; auto-generates client ID on first launch; handles token refresh |
| PlayncClient.cs | HTTP client for Plaync API (character skill levels, CP, combat score) |
| SkillLevelCache.cs | Caches skill level lookups (keyed by character name + server ID); async fetch with expiry |
| EquipmentCache.cs | Caches equipment data (possibly unused; for future enhancement) |
| SkillIconCache.cs | Caches skill icon textures (possibly unused; for future enhancement) |
| CharacterData.cs | DTO for character enrichment data |
| JsonExtensions.cs | JSON serialization helpers |

## AI Agent Instructions

- **SkillLevelCache**: Thread-safe cache; EnsureLoaded() triggers async fetch if not cached. Party members are auto-fetched when detected in DpsPipeline.
- **WebUploader**: Token stored in %APPDATA%\A2Meter\web_token; client ID auto-generated. Fire-and-forget; failures are swallowed (best-effort).
- **PlayncClient**: May require reverse-engineering if API contract changes. Check actual requests in SkillLevelCache.EnsureLoaded().
- For enrichment flow: DpsPipeline.SaveRecord() looks up CP/Score via SkillLevelCache.Get(); if missing, triggers async fetch for next session.

## Dependencies

- System.Net.Http (HTTP requests)
- System.Text.Json (JSON parsing)
- A2Meter.Dps (CombatRecord DTO)

## Notes

- Plaync API calls are async; cached results avoid repeated requests.
- Web upload is opt-in; disabled by default in AppSettings (WebUploadEnabled).
